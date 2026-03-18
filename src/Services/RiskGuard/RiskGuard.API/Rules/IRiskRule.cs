namespace RiskGuard.API.Rules;

/// <summary>
/// A single, independently testable risk check.
/// Rules are evaluated in registration order; the first rejection short-circuits the chain.
/// </summary>
public interface IRiskRule
{
    string Name { get; }

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
    decimal TakeProfit);

public sealed record RuleResult
{
    public bool IsRejected { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal? AdjustedQuantity { get; init; }

    public static RuleResult Pass() => new();
    public static RuleResult Reject(string reason) => new() { IsRejected = true, Reason = reason };
    public static RuleResult AdjustQuantity(decimal qty) => new() { AdjustedQuantity = qty };
}
