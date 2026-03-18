namespace RiskGuard.API.Rules;

/// <summary>Rejects orders with zero/negative quantity or no entry price.</summary>
public sealed class QuantityRule : IRiskRule
{
    public string Name => nameof(QuantityRule);

    public ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.Quantity <= 0)
            return ValueTask.FromResult(RuleResult.Reject("Quantity must be greater than zero."));

        if (context.EntryPrice <= 0)
            return ValueTask.FromResult(RuleResult.Reject("Entry price must be greater than zero."));

        return ValueTask.FromResult(RuleResult.Pass());
    }
}
