using Microsoft.Extensions.Options;
using Strategy.Worker.Configuration;

namespace Strategy.Worker.Infrastructure;

/// <summary>
/// Mutable in-memory store for strategy settings that can be hot-reloaded from Redis.
/// Initialized from appsettings; overridden at runtime by StrategyConfigWatcher.
/// </summary>
public sealed class StrategyConfigStore
{
    private readonly object _sync = new();
    private decimal _defaultOrderNotionalUsdt;
    private decimal _minOrderNotionalUsdt;

    public StrategyConfigStore(IOptions<TradingSettings> settings)
    {
        _defaultOrderNotionalUsdt = settings.Value.DefaultOrderNotionalUsdt;
        _minOrderNotionalUsdt = settings.Value.MinOrderNotionalUsdt;
    }

    public decimal DefaultOrderNotionalUsdt
    {
        get { lock (_sync) return _defaultOrderNotionalUsdt; }
    }

    public decimal MinOrderNotionalUsdt
    {
        get { lock (_sync) return _minOrderNotionalUsdt; }
    }

    public void Update(decimal? defaultOrderNotional, decimal? minOrderNotional)
    {
        lock (_sync)
        {
            if (defaultOrderNotional.HasValue && defaultOrderNotional.Value > 0)
                _defaultOrderNotionalUsdt = defaultOrderNotional.Value;
            if (minOrderNotional.HasValue && minOrderNotional.Value >= 0)
                _minOrderNotionalUsdt = minOrderNotional.Value;
        }
    }
}
