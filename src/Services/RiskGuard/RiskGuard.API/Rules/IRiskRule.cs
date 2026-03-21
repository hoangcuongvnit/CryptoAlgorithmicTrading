namespace RiskGuard.API.Rules;

/// <summary>
/// A single, independently testable risk check.
/// Rules are evaluated in registration order; the first rejection short-circuits the chain.
/// </summary>
public interface IRiskRule
{
    string Name { get; }

    /// <summary>Semantic version of this rule implementation. Defaults to "1.0".</summary>
    string Version => "1.0";

    /// <summary>
    /// Evaluate the rule.
    /// Returns <see cref="RuleResult.Pass()"/> to allow the order through,
    /// <see cref="RuleResult.Reject"/> to block it, or
    /// <see cref="RuleResult.AdjustQuantity"/> to allow with a reduced quantity.
    /// </summary>
    ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default);
}

/// <summary>Immutable snapshot of an order request passed to each rule.</summary>
public sealed record RiskContext(
    string Symbol,
    string Side,
    decimal Quantity,
    decimal EntryPrice,
    decimal StopLoss,
    decimal TakeProfit,
    string? SessionId = null,
    string? SessionPhase = null,
    bool IsReduceOnly = false);

public sealed record RuleResult
{
    public bool IsRejected { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal? AdjustedQuantity { get; init; }

    /// <summary>Short machine-readable code for the rejection or adjustment (optional).</summary>
    public string? ReasonCode { get; init; }

    /// <summary>The configured threshold that was violated (optional, for UI display).</summary>
    public string? ThresholdValue { get; init; }

    /// <summary>The actual value that triggered this result (optional, for UI display).</summary>
    public string? ActualValue { get; init; }

    public static RuleResult Pass() => new();

    public static RuleResult Reject(
        string reason,
        string? reasonCode = null,
        string? thresholdValue = null,
        string? actualValue = null)
        => new()
        {
            IsRejected = true,
            Reason = reason,
            ReasonCode = reasonCode,
            ThresholdValue = thresholdValue,
            ActualValue = actualValue
        };

    public static RuleResult AdjustQuantity(
        decimal qty,
        string? reasonCode = null,
        string? actualValue = null)
        => new()
        {
            AdjustedQuantity = qty,
            ReasonCode = reasonCode,
            ActualValue = actualValue
        };
}
