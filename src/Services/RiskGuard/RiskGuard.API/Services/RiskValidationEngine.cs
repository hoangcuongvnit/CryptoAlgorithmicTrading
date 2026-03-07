using System.Collections.Concurrent;
using RiskGuard.API.Configuration;
using Microsoft.Extensions.Options;

namespace RiskGuard.API.Services;

public sealed class RiskValidationEngine
{
    private readonly RiskSettings _settings;
    private readonly ConcurrentDictionary<string, DateTime> _lastSymbolOrderTime = new(StringComparer.OrdinalIgnoreCase);

    public RiskValidationEngine(IOptions<RiskSettings> settings)
    {
        _settings = settings.Value;
    }

    public RiskEvaluationResult Evaluate(
        string symbol,
        string side,
        decimal quantity,
        decimal entryPrice,
        decimal stopLoss,
        decimal takeProfit)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return RiskEvaluationResult.Reject("Symbol is required.");
        }

        if (_settings.AllowedSymbols.Count > 0 && !_settings.AllowedSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
        {
            return RiskEvaluationResult.Reject($"Symbol {symbol} is not allowed.");
        }

        if (quantity <= 0)
        {
            return RiskEvaluationResult.Reject("Quantity must be greater than zero.");
        }

        if (entryPrice <= 0)
        {
            return RiskEvaluationResult.Reject("Entry price must be greater than zero.");
        }

        var notional = quantity * entryPrice;
        if (_settings.MaxOrderNotional > 0 && notional > _settings.MaxOrderNotional)
        {
            var adjustedQuantity = _settings.MaxOrderNotional / entryPrice;
            return RiskEvaluationResult.Approve(decimal.Round(adjustedQuantity, 8, MidpointRounding.AwayFromZero));
        }

        if (!string.IsNullOrWhiteSpace(side) && stopLoss > 0 && takeProfit > 0)
        {
            var rr = CalculateRiskReward(side, entryPrice, stopLoss, takeProfit);
            if (rr <= 0 || rr < _settings.MinRiskReward)
            {
                return RiskEvaluationResult.Reject($"Risk/reward ratio {rr:0.##} is below minimum {_settings.MinRiskReward:0.##}.");
            }
        }

        var now = DateTime.UtcNow;
        if (_lastSymbolOrderTime.TryGetValue(symbol, out var lastOrderAt))
        {
            var elapsed = now - lastOrderAt;
            if (_settings.CooldownSeconds > 0 && elapsed < TimeSpan.FromSeconds(_settings.CooldownSeconds))
            {
                return RiskEvaluationResult.Reject($"Cooldown active for {symbol}. Try again in {Math.Ceiling((_settings.CooldownSeconds - elapsed.TotalSeconds)):0}s.");
            }
        }

        _lastSymbolOrderTime[symbol] = now;
        return RiskEvaluationResult.Approve(quantity);
    }

    private static decimal CalculateRiskReward(string side, decimal entry, decimal stopLoss, decimal takeProfit)
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

public sealed record RiskEvaluationResult(bool IsApproved, string RejectionReason, decimal AdjustedQuantity)
{
    public static RiskEvaluationResult Approve(decimal adjustedQuantity)
        => new(true, string.Empty, adjustedQuantity);

    public static RiskEvaluationResult Reject(string reason)
        => new(false, reason, 0m);
}
