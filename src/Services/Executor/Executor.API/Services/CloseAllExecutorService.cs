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
    private readonly CloseAllDiscoveryService _discovery;
    private readonly OrderExecutionService _executionService;
    private readonly ILogger<CloseAllExecutorService> _logger;

    public CloseAllExecutorService(
        ShutdownOperationService shutdownOp,
        PositionTracker positionTracker,
        CloseAllDiscoveryService discovery,
        OrderExecutionService executionService,
        ILogger<CloseAllExecutorService> logger)
    {
        _shutdownOp = shutdownOp;
        _positionTracker = positionTracker;
        _discovery = discovery;
        _executionService = executionService;
        _logger = logger;
    }

    public async Task ExecuteCloseAllAsync(Guid operationId, CancellationToken ct)
    {
        _shutdownOp.TransitionToExecuting(operationId);
        _logger.LogInformation("CloseAll execution started: operationId={OperationId}", operationId);

        var localPositions = _positionTracker.GetRawPositions();
        var plan = await _discovery.BuildClosePlanAsync(localPositions, ct);
        var targets = plan.Targets;

        int closedCount = 0;
        int attemptedCloseCount = 0;
        var errors = new List<string>();
        var attemptedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (string.IsNullOrWhiteSpace(target.Symbol) || target.Quantity <= 0m)
                    continue;

                attemptedCloseCount++;
                attemptedSymbols.Add(target.Symbol);

                var closeOrder = new OrderRequest
                {
                    Symbol = target.Symbol,
                    Side = OrderSide.Sell,
                    Type = OrderType.Market,
                    Quantity = target.Quantity,
                    IsReduceOnly = true,
                    StrategyName = "ShutdownControl"
                };

                var result = await _executionService.ExecuteOrderAsync(closeOrder, ct);

                if (result.Success)
                {
                    closedCount++;
                    _logger.LogInformation(
                        "ShutdownControl closed {Symbol}: qty={Qty} price={Price} source={Source}",
                        target.Symbol, result.FilledQty, result.FilledPrice, target.Source);
                }
                else
                {
                    errors.Add($"{target.Symbol}: {result.ErrorMessage}");
                    _logger.LogWarning(
                        "ShutdownControl failed to close {Symbol}: {Error}",
                        target.Symbol, result.ErrorMessage);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var label = target.Symbol;
                errors.Add($"{label}: {ex.Message}");
                _logger.LogError(ex, "Error closing position {Symbol}", label);
            }
        }

        var leftovers = await _discovery.VerifyLeftoversAsync(attemptedSymbols, plan.PreCloseLeftovers, ct);
        var verifiedAtUtc = DateTime.UtcNow;

        // Verify final state
        var remaining = _positionTracker.GetOpenPositions();

        if (remaining.Count == 0 && errors.Count == 0 && leftovers.Count == 0)
        {
            _shutdownOp.TransitionToCompleted(
                operationId,
                closedCount,
                discoveredCandidatesCount: plan.DiscoveredCandidatesCount,
                attemptedCloseCount: attemptedCloseCount,
                verifiedAtUtc: verifiedAtUtc,
                leftovers: leftovers);
            _logger.LogInformation(
                "CloseAll COMPLETED: operationId={OperationId} closed={Count} attempted={Attempted} discovered={Discovered}. System is shutdown-ready.",
                operationId, closedCount, attemptedCloseCount, plan.DiscoveredCandidatesCount);
        }
        else
        {
            var errorSummary = errors.Count > 0
                ? string.Join("; ", errors)
                : leftovers.Count > 0
                    ? $"{leftovers.Count} leftover asset(s) remain after verification"
                    : $"{remaining.Count} position(s) still open after close attempts";

            _shutdownOp.TransitionToCompletedWithErrors(
                operationId,
                closedCount,
                errorSummary,
                discoveredCandidatesCount: plan.DiscoveredCandidatesCount,
                attemptedCloseCount: attemptedCloseCount,
                verifiedAtUtc: verifiedAtUtc,
                leftovers: leftovers);
        }
    }
}
