using CryptoTrading.Shared.DTOs;
using Microsoft.Extensions.Options;
using RiskGuard.API.Configuration;
using RiskGuard.API.Infrastructure;
using StackExchange.Redis;

namespace RiskGuard.API.Rules;

/// <summary>
/// Caps a single order's notional value as a percentage of the virtual account balance.
/// When the order exceeds the cap, quantity is adjusted down rather than rejecting outright.
/// Falls back to the absolute <see cref="RiskSettings.MaxOrderNotional"/> when
/// <see cref="RiskSettings.MaxPositionSizePercent"/> is not set.
///
/// Phase 2.3: When <see cref="RiskSettings.KellyCriterionEnabled"/> is true, applies
/// quarter-Kelly sizing based on historical win-rate from the orders table.
///
/// Phase 3.1: When a HighVolatility regime is detected in Redis, reduces the cap further.
/// </summary>
public sealed class PositionSizeRule : IRiskRule
{
    private readonly RiskSettings _settings;
    private readonly OrderStatsRepository _statsRepo;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PositionSizeRule> _logger;

    public string Name => nameof(PositionSizeRule);

    public PositionSizeRule(
        IOptions<RiskSettings> settings,
        OrderStatsRepository statsRepo,
        IConnectionMultiplexer redis,
        ILogger<PositionSizeRule> logger)
    {
        _settings = settings.Value;
        _statsRepo = statsRepo;
        _redis = redis;
        _logger = logger;
    }

    public async ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        if (context.EntryPrice <= 0)
            return RuleResult.Pass();

        var maxNotional = await ComputeMaxNotionalAsync(context.Symbol, ct);
        if (maxNotional <= 0)
            return RuleResult.Pass();

        var orderNotional = context.Quantity * context.EntryPrice;
        if (orderNotional <= maxNotional)
            return RuleResult.Pass();

        var adjustedQty = decimal.Round(
            maxNotional / context.EntryPrice, 8, MidpointRounding.AwayFromZero);

        return RuleResult.AdjustQuantity(adjustedQty);
    }

    private async Task<decimal> ComputeMaxNotionalAsync(string symbol, CancellationToken ct)
    {
        // Base cap: percentage-of-balance takes precedence over flat notional
        decimal basePercent = _settings.MaxPositionSizePercent > 0
            ? _settings.MaxPositionSizePercent
            : 2.0m;

        // Phase 2.3: Quarter-Kelly override
        if (_settings.KellyCriterionEnabled)
        {
            try
            {
                var stats = await _statsRepo.GetWinRateAsync(
                    symbol, _settings.KellyLookbackDays, ct);

                if (stats.TotalTrades >= _settings.KellyMinTrades && stats.AvgLoss > 0)
                {
                    var b = stats.AvgWin / stats.AvgLoss;   // reward/risk ratio
                    var p = stats.WinRate;
                    var q = 1m - p;
                    var fullKelly = b > 0 ? (b * p - q) / b : 0m;
                    var kellyPercent = Math.Max(0m, fullKelly * _settings.KellyFraction * 100m);

                    // Kelly wins if smaller than the configured cap (always protects capital)
                    basePercent = Math.Min(basePercent, kellyPercent > 0 ? kellyPercent : basePercent);
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Kelly sizing DB lookup failed for {Symbol}; using fixed cap", symbol); }
        }

        // Phase 3.1: Reduce size in high-volatility regime
        try
        {
            var db = _redis.GetDatabase();
            var regimeKey = _settings.RegimeRedisKeyPrefix + symbol;
            var regimeVal = (string?)await db.StringGetAsync(regimeKey);
            if (regimeVal == nameof(MarketRegime.HighVolatility))
                basePercent *= _settings.HighVolPositionSizeMultiplier;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Regime lookup failed for {Symbol}; skipping regime adjustment", symbol); }

        if (_settings.VirtualAccountBalance > 0)
            return basePercent / 100m * _settings.VirtualAccountBalance;

        return _settings.MaxOrderNotional;
    }
}
