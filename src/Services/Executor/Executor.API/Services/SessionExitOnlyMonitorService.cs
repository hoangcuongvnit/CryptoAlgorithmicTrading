using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Session;

namespace Executor.API.Services;

/// <summary>
/// Background service that monitors session boundaries and automatically
/// toggles exit-only mode during the final 30 minutes of each trading session.
/// Evaluates every 1 minute (configurable).
/// </summary>
public sealed class SessionExitOnlyMonitorService : BackgroundService
{
    private readonly SessionClock _sessionClock;
    private readonly ShutdownOperationService _shutdownOp;
    private readonly ILogger<SessionExitOnlyMonitorService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    public SessionExitOnlyMonitorService(
        SessionClock sessionClock,
        ShutdownOperationService shutdownOp,
        ILogger<SessionExitOnlyMonitorService> logger)
    {
        _sessionClock = sessionClock;
        _shutdownOp = shutdownOp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SessionExitOnlyMonitorService started — checking every {Interval}s", CheckInterval.TotalSeconds);

        // Initial evaluation on startup
        EvaluateSessionExitOnly();

        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            EvaluateSessionExitOnly();
        }
    }

    private void EvaluateSessionExitOnly()
    {
        try
        {
            var session = _sessionClock.GetCurrentSession();
            var inFinal30 = IsInFinal30Minutes(session);

            if (inFinal30)
            {
                _shutdownOp.EnterSessionExitOnly();
            }
            else
            {
                _shutdownOp.ReleaseSessionExitOnly();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate session exit-only status");
        }
    }

    private static bool IsInFinal30Minutes(SessionInfo session)
    {
        // LiquidationOnly, ForcedFlatten phases are within the final 30-minute window
        return session.CurrentPhase is SessionPhase.LiquidationOnly or SessionPhase.ForcedFlatten;
    }
}
