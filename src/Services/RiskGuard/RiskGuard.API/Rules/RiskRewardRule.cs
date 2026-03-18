using Microsoft.Extensions.Options;
using RiskGuard.API.Configuration;

namespace RiskGuard.API.Rules;

/// <summary>
/// Requires the order's risk/reward ratio to meet the configured minimum.
/// Skipped silently when stop-loss or take-profit are not provided.
/// </summary>
public sealed class RiskRewardRule : IRiskRule
{
    private readonly RiskSettings _settings;

    public string Name => nameof(RiskRewardRule);

    public RiskRewardRule(IOptions<RiskSettings> settings)
        => _settings = settings.Value;

    public ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        // Can't evaluate without both SL and TP
        if (context.StopLoss <= 0 || context.TakeProfit <= 0 || context.EntryPrice <= 0)
            return ValueTask.FromResult(RuleResult.Pass());

        var rr = CalculateRiskReward(context.Side, context.EntryPrice, context.StopLoss, context.TakeProfit);

        if (rr <= 0 || rr < _settings.MinRiskReward)
        {
            return ValueTask.FromResult(RuleResult.Reject(
                $"Risk/reward ratio {rr:F2} is below the minimum {_settings.MinRiskReward:F2}."));
        }

        return ValueTask.FromResult(RuleResult.Pass());
    }

    private static decimal CalculateRiskReward(
        string side, decimal entry, decimal stopLoss, decimal takeProfit)
    {
        if (side.Equals("Buy", StringComparison.OrdinalIgnoreCase))
        {
            var risk = entry - stopLoss;
            var reward = takeProfit - entry;
            return risk <= 0 ? 0 : reward / risk;
        }

        if (side.Equals("Sell", StringComparison.OrdinalIgnoreCase))
        {
            var risk = stopLoss - entry;
            var reward = entry - takeProfit;
            return risk <= 0 ? 0 : reward / risk;
        }

        return 0;
    }
}
