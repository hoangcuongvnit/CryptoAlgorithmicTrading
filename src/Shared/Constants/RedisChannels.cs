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
    /// Redis string key storing the current open position quantity for a symbol: position:{symbol}:qty
    /// Value is a decimal string e.g. "0.00004000". "0" = position closed (distinct from missing key).
    /// Missing key = never published or expired → fail-open in consumers.
    /// Example: position:BTCUSDT:qty
    /// </summary>
    public static string PositionQty(string symbol) => $"position:{symbol}:qty";

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

    /// <summary>
    /// Redis string key storing the active IANA timezone for all services: system:config:timezone
    /// Example value: "Asia/Ho_Chi_Minh"
    /// </summary>
    public const string SystemConfigTimezone = "system:config:timezone";

    /// <summary>
    /// Pub/Sub channel broadcast when system config changes: system:config:changed
    /// Message payload is the new IANA timezone string.
    /// </summary>
    public const string SystemConfigChanged = "system:config:changed";

    /// <summary>Redis string key: default order notional in USDT for Strategy service.</summary>
    public const string StrategyConfigDefaultOrderNotional = "strategy:config:defaultOrderNotionalUsdt";

    /// <summary>Redis string key: minimum order notional in USDT for Strategy service.</summary>
    public const string StrategyConfigMinOrderNotional = "strategy:config:minOrderNotionalUsdt";

    /// <summary>Pub/Sub channel broadcast when strategy config changes: strategy:config:changed</summary>
    public const string StrategyConfigChanged = "strategy:config:changed";
}
