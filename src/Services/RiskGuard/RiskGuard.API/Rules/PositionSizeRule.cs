using Microsoft.Extensions.Options;
using RiskGuard.API.Configuration;

namespace RiskGuard.API.Rules;

/// <summary>
/// Enforces fixed USDT notional bounds on each order.
/// Orders below <see cref="RiskSettings.MinOrderNotional"/> are rejected.
/// Orders above <see cref="RiskSettings.MaxOrderNotional"/> have their quantity trimmed down.
/// </summary>
public sealed class PositionSizeRule : IRiskRule
{
    private readonly RiskSettings _settings;
    private readonly ILogger<PositionSizeRule> _logger;

    public string Name => nameof(PositionSizeRule);

    public PositionSizeRule(IOptions<RiskSettings> settings, ILogger<PositionSizeRule> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.EntryPrice <= 0)
            return ValueTask.FromResult(RuleResult.Pass());

        var orderNotional = context.Quantity * context.EntryPrice;

        if (_settings.MinOrderNotional > 0 && orderNotional < _settings.MinOrderNotional)
        {
            _logger.LogInformation(
                "PositionSizeRule: {Symbol} notional {Notional:F4} USDT is below minimum {Min:F4} USDT — rejecting",
                context.Symbol, orderNotional, _settings.MinOrderNotional);
            return ValueTask.FromResult(RuleResult.Reject(
                $"Order notional {orderNotional:F4} USDT is below minimum {_settings.MinOrderNotional:F4} USDT"));
        }

        if (_settings.MaxOrderNotional > 0 && orderNotional > _settings.MaxOrderNotional)
        {
            var adjustedQty = decimal.Round(
                _settings.MaxOrderNotional / context.EntryPrice, 8, MidpointRounding.AwayFromZero);
            _logger.LogInformation(
                "PositionSizeRule: {Symbol} notional {Notional:F4} USDT exceeds max {Max:F4} USDT — trimming qty to {Qty}",
                context.Symbol, orderNotional, _settings.MaxOrderNotional, adjustedQty);
            return ValueTask.FromResult(RuleResult.AdjustQuantity(adjustedQty));
        }

        return ValueTask.FromResult(RuleResult.Pass());
    }
}
