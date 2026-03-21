namespace Executor.API.Services;

/// <summary>
/// Background service that watches for scheduled close-all operations and triggers them
/// at the configured time. Also restores any persisted schedule from Redis on startup.
/// </summary>
public sealed class CloseAllSchedulerService : BackgroundService
{
    private readonly ShutdownOperationService _shutdownOp;
    private readonly CloseAllExecutorService _closeAllExecutor;
    private readonly ILogger<CloseAllSchedulerService> _logger;

    public CloseAllSchedulerService(
        ShutdownOperationService shutdownOp,
        CloseAllExecutorService closeAllExecutor,
        ILogger<CloseAllSchedulerService> logger)
    {
        _shutdownOp = shutdownOp;
        _closeAllExecutor = closeAllExecutor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Restore any persisted scheduled operation from Redis before polling starts
        await _shutdownOp.LoadFromRedisAsync();

        _logger.LogInformation("CloseAllSchedulerService started.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckScheduledOperationAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "CloseAllSchedulerService cycle error");
            }
        }
    }

    private async Task CheckScheduledOperationAsync(CancellationToken ct)
    {
        var op = _shutdownOp.Current;

        if (op.Status != "Scheduled" || op.ScheduledForUtc is null)
            return;

        if (op.ScheduledForUtc.Value > DateTime.UtcNow)
            return;

        _logger.LogInformation(
            "Triggering scheduled CloseAll: operationId={OperationId} scheduledFor={ScheduledFor}",
            op.OperationId, op.ScheduledForUtc);

        // Execute without the request cancellation token so a service stop doesn't abort mid-flatten
        await _closeAllExecutor.ExecuteCloseAllAsync(op.OperationId, CancellationToken.None);
    }
}
