namespace CryptoTrading.Shared.Constants;

public static class RedisChannels
{
    /// <summary>
    /// Price tick channel pattern: price:{symbol}
    /// Example: price:BTCUSDT
    /// </summary>
    public static string Price(string symbol) => $"price:{symbol}";

    /// <summary>
    /// Signal channel pattern: signal:{symbol}
    /// Example: signal:BTCUSDT
    /// </summary>
    public static string Signal(string symbol) => $"signal:{symbol}";

    /// <summary>
    /// Trades audit log channel: trades:audit
    /// </summary>
    public const string TradesAudit = "trades:audit";

    /// <summary>
    /// System events channel: system:events
    /// </summary>
    public const string SystemEvents = "system:events";

    /// <summary>
    /// Symbol configuration changes channel: config:symbols
    /// </summary>
    public const string ConfigSymbols = "config:symbols";

    /// <summary>
    /// Price channel pattern for subscription: price:*
    /// </summary>
    public const string PricePattern = "price:*";

    /// <summary>
    /// Signal channel pattern for subscription: signal:*
    /// </summary>
    public const string SignalPattern = "signal:*";

    /// <summary>
    /// Timeline log channel per symbol: coin:{symbol}:log
    /// </summary>
    public static string TimelineLog(string symbol) => $"coin:{symbol}:log";

    /// <summary>
    /// Timeline log channel pattern for subscription: coin:*:log
    /// </summary>
    public const string TimelinePattern = "coin:*:log";

    /// <summary>
    /// Session lifecycle events channel: session:events
    /// </summary>
    public const string SessionEvents = "session:events";
}
