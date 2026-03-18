using Microsoft.Extensions.Options;
using RiskGuard.API.Configuration;

namespace RiskGuard.API.Rules;

/// <summary>
/// Caps a single order's notional value as a percentage of the virtual account balance.
/// When the order exceeds the cap, quantity is adjusted down rather than rejecting outright.
/// Falls back to the absolute <see cref="RiskSettings.MaxOrderNotional"/> when
/// <see cref="RiskSettings.MaxPositionSizePercent"/> is not set.
/// </summary>
public sealed class PositionSizeRule : IRiskRule
{
    private readonly RiskSettings _settings;

    public string Name => nameof(PositionSizeRule);

    public PositionSizeRule(IOptions<RiskSettings> settings)
        => _settings = settings.Value;

    public ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.EntryPrice <= 0)
            return ValueTask.FromResult(RuleResult.Pass());

        var maxNotional = ComputeMaxNotional();
        if (maxNotional <= 0)
            return ValueTask.FromResult(RuleResult.Pass());

        var orderNotional = context.Quantity * context.EntryPrice;
        if (orderNotional <= maxNotional)
            return ValueTask.FromResult(RuleResult.Pass());

        // Adjust quantity down to fit within position-size cap
        var adjustedQty = decimal.Round(
            maxNotional / context.EntryPrice, 8, MidpointRounding.AwayFromZero);

        return ValueTask.FromResult(RuleResult.AdjustQuantity(adjustedQty));
    }

    private decimal ComputeMaxNotional()
    {
        // Percentage-of-balance cap takes precedence over the flat notional cap
        if (_settings.MaxPositionSizePercent > 0 && _settings.VirtualAccountBalance > 0)
            return _settings.MaxPositionSizePercent / 100m * _settings.VirtualAccountBalance;

        return _settings.MaxOrderNotional;
    }
}
