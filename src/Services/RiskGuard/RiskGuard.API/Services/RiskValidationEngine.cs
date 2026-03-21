using RiskGuard.API.Rules;

namespace RiskGuard.API.Services;

/// <summary>
/// Runs each registered <see cref="IRiskRule"/> in order.
/// The first rejection short-circuits the chain.
/// Quantity adjustments from one rule are propagated to all subsequent rules.
/// </summary>
public sealed class RiskValidationEngine
{
    private readonly IReadOnlyList<IRiskRule> _rules;
    private readonly ValidationHistory _history;
    private readonly ILogger<RiskValidationEngine> _logger;

    public RiskValidationEngine(
        IEnumerable<IRiskRule> rules,
        ValidationHistory history,
        ILogger<RiskValidationEngine> logger)
    {
        _rules = rules.ToList();
        _history = history;
        _logger = logger;
    }

    public async Task<RiskEvaluationResult> EvaluateAsync(
        string symbol,
        string side,
        decimal quantity,
        decimal entryPrice,
        decimal stopLoss,
        decimal takeProfit,
        CancellationToken ct = default,
        string? sessionId = null,
        string? sessionPhase = null,
        bool isReduceOnly = false)
    {
        var effectiveQty = quantity;

        foreach (var rule in _rules)
        {
            var context = new RiskContext(symbol, side, effectiveQty, entryPrice, stopLoss, takeProfit,
                sessionId, sessionPhase, isReduceOnly);
            var result = await rule.EvaluateAsync(context, ct);

            if (result.IsRejected)
            {
                _logger.LogDebug(
                    "[{Rule}] rejected {Symbol} {Side}: {Reason}",
                    rule.Name, symbol, side, result.Reason);
                _history.Record(symbol, side, approved: false, rejectionReason: result.Reason);
                return RiskEvaluationResult.Reject(result.Reason);
            }

            if (result.AdjustedQuantity.HasValue)
            {
                _logger.LogDebug(
                    "[{Rule}] adjusted qty for {Symbol}: {Old} → {New}",
                    rule.Name, symbol, effectiveQty, result.AdjustedQuantity.Value);
                effectiveQty = result.AdjustedQuantity.Value;
            }
        }

        _history.Record(symbol, side, approved: true, rejectionReason: string.Empty);
        return RiskEvaluationResult.Approve(effectiveQty);
    }
}

public sealed record RiskEvaluationResult(bool IsApproved, string RejectionReason, decimal AdjustedQuantity)
{
    public static RiskEvaluationResult Approve(decimal adjustedQuantity)
        => new(true, string.Empty, adjustedQuantity);

    public static RiskEvaluationResult Reject(string reason)
        => new(false, reason, 0m);
}
