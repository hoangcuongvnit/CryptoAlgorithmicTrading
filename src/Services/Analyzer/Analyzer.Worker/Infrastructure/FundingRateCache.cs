using System.Collections.Concurrent;

namespace Analyzer.Worker.Infrastructure;

/// <summary>
/// In-memory cache of the latest perpetual futures funding rates per symbol,
/// refreshed by <see cref="Workers.FundingRateFetcherWorker"/> every hour.
/// Used by the IndicatorEngine to gate short-selling signals.
/// </summary>
public sealed class FundingRateCache
{
    private readonly ConcurrentDictionary<string, decimal> _rates = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string symbol, decimal rate) => _rates[symbol] = rate;

    /// <summary>Returns the cached funding rate, or 0 if not yet populated.</summary>
    public decimal Get(string symbol) =>
        _rates.TryGetValue(symbol, out var rate) ? rate : 0m;
}
