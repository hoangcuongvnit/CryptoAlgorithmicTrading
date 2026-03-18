using CryptoTrading.Shared.DTOs;
using Skender.Stock.Indicators;
using System.Collections.Concurrent;

namespace Analyzer.Worker.Analysis;

/// <summary>
/// Thread-safe rolling buffer of Quote objects per symbol.
/// Stores the most recent <see cref="Capacity"/> completed candles.
/// </summary>
public sealed class PriceBuffer
{
    private readonly int _capacity;
    private readonly ConcurrentDictionary<string, List<Quote>> _buffers = new();

    public int Capacity => _capacity;

    public PriceBuffer(int capacity = 60)
    {
        _capacity = capacity;
    }

    public void Add(PriceTick tick)
    {
        var buffer = _buffers.GetOrAdd(tick.Symbol, _ => new List<Quote>(_capacity + 1));

        var quote = new Quote
        {
            Date = new DateTime(
                tick.Timestamp.Year, tick.Timestamp.Month, tick.Timestamp.Day,
                tick.Timestamp.Hour, tick.Timestamp.Minute, 0, DateTimeKind.Utc),
            Open = tick.Open,
            High = tick.High,
            Low = tick.Low,
            Close = tick.Close > 0 ? tick.Close : tick.Price,
            Volume = tick.Volume
        };

        lock (buffer)
        {
            // Replace any existing candle for the same minute
            buffer.RemoveAll(q => q.Date == quote.Date);
            buffer.Add(quote);

            if (buffer.Count > _capacity)
                buffer.RemoveAt(0);
        }
    }

    /// <summary>Returns a snapshot of quotes sorted oldest-first, or null if symbol not yet seen.</summary>
    public IReadOnlyList<Quote>? GetQuotes(string symbol)
    {
        if (!_buffers.TryGetValue(symbol, out var buffer))
            return null;

        lock (buffer)
        {
            return [.. buffer.OrderBy(q => q.Date)];
        }
    }

    public int Count(string symbol)
    {
        if (!_buffers.TryGetValue(symbol, out var buffer))
            return 0;

        lock (buffer)
        {
            return buffer.Count;
        }
    }
}
