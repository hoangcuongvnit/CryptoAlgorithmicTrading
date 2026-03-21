using CryptoTrading.Shared.DTOs;
using System.Globalization;

namespace HistoricalCollector.Worker.Parsers;

public static class KlineCsvParser
{
    private const long MaxUnixMilliseconds = 253402300799999;

    public static bool TryParseLine(string line, string symbol, string interval, out PriceTick? tick)
    {
        tick = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.Split(',');
        if (parts.Length < 6)
        {
            return false;
        }

        if (!long.TryParse(parts[0], out var openTimeMs))
        {
            return false;
        }

        openTimeMs = NormalizeToUnixMilliseconds(openTimeMs);
        if (openTimeMs < 0 || openTimeMs > MaxUnixMilliseconds)
        {
            return false;
        }

        if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var open) ||
            !decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var high) ||
            !decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var low) ||
            !decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var close) ||
            !decimal.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
        {
            return false;
        }

        DateTime timestamp;
        try
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        tick = new PriceTick
        {
            Symbol = symbol,
            Price = close,
            Volume = volume,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Timestamp = timestamp,
            Interval = interval
        };

        return true;
    }

    private static long NormalizeToUnixMilliseconds(long timestamp)
    {
        while (timestamp > MaxUnixMilliseconds)
        {
            timestamp /= 1000;
        }

        return timestamp;
    }
}
