using CryptoTrading.Shared.DTOs;
using Executor.API.Infrastructure;

namespace Executor.API.Services;

/// <summary>
/// Executes the close-all flatten operation by placing reduce-only market close orders
/// for every open position tracked by PositionTracker.
/// Reuses OrderExecutionService (same pipeline as LiquidationOrchestrator).
/// </summary>
public sealed class CloseAllExecutorService
{
    private readonly ShutdownOperationService _shutdownOp;
    private readonly PositionTracker _positionTracker;
    private readonly OrderExecutionService _executionService;
    private readonly ILogger<CloseAllExecutorService> _logger;

    public CloseAllExecutorService(
        ShutdownOperationService shutdownOp,
        PositionTracker positionTracker,
        OrderExecutionService executionService,
        ILogger<CloseAllExecutorService> logger)
    {
        _shutdownOp = shutdownOp;
        _positionTracker = positionTracker;
        _executionService = executionService;
        _logger = logger;
    }

    public async Task ExecuteCloseAllAsync(Guid operationId, CancellationToken ct)
    {
        _shutdownOp.TransitionToExecuting(operationId);
        _logger.LogInformation("CloseAll execution started: operationId={OperationId}", operationId);

        var positions = _positionTracker.GetOpenPositions();
        int closedCount = 0;
        var errors = new List<string>();

        foreach (var pos in positions)
        {
            if (ct.IsCancellationRequested) break;

            string? symbol = null;
            try
            {
                symbol = pos.GetType().GetProperty("symbol")?.GetValue(pos)?.ToString();
                var quantity = (decimal)(pos.GetType().GetProperty("quantity")?.GetValue(pos) ?? 0m);
                var currentPrice = (decimal)(pos.GetType().GetProperty("currentPrice")?.GetValue(pos) ?? 0m);

                if (string.IsNullOrEmpty(symbol) || quantity <= 0)
                    continue;

                var closeOrder = new OrderRequest
                {
                    Symbol = symbol,
                    Side = OrderSide.Sell,
                    Type = OrderType.Market,
                    Quantity = quantity,
                    Price = currentPrice,
                    IsReduceOnly = true,
                    StrategyName = "ShutdownControl"
                };

                var result = await _executionService.ExecuteOrderAsync(closeOrder, ct);

                if (result.Success)
                {
                    closedCount++;
                    _logger.LogInformation(
                        "ShutdownControl closed {Symbol}: qty={Qty} price={Price}",
                        symbol, result.FilledQty, result.FilledPrice);
                }
                else
                {
                    errors.Add($"{symbol}: {result.ErrorMessage}");
                    _logger.LogWarning(
                        "ShutdownControl failed to close {Symbol}: {Error}",
                        symbol, result.ErrorMessage);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var label = symbol ?? "UNKNOWN";
                errors.Add($"{label}: {ex.Message}");
                _logger.LogError(ex, "Error closing position {Symbol}", label);
            }
        }

        // Verify final state
        var remaining = _positionTracker.GetOpenPositions();

        if (remaining.Count == 0 && errors.Count == 0)
        {
            _shutdownOp.TransitionToCompleted(operationId, closedCount);
            _logger.LogInformation(
                "CloseAll COMPLETED: operationId={OperationId} closed={Count}. System is shutdown-ready.",
                operationId, closedCount);
        }
        else
        {
            var errorSummary = errors.Count > 0
                ? string.Join("; ", errors)
                : $"{remaining.Count} position(s) still open after close attempts";
            _shutdownOp.TransitionToCompletedWithErrors(operationId, closedCount, errorSummary);
        }
    }
}
