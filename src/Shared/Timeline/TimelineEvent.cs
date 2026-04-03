namespace CryptoTrading.Shared.Timeline;

public sealed record TimelineEvent
{
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string SourceService { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string? SessionId { get; init; }
    public string Severity { get; init; } = "INFO";
    public Dictionary<string, object?> Payload { get; init; } = new();
    public Dictionary<string, object?> Metadata { get; init; } = new();
    public List<string> Tags { get; init; } = new();
}

public static class TimelineEventTypes
{
    // Ingestor
    public const string PriceTickReceived = "PRICE_TICK_RECEIVED";

    // Analyzer
    public const string SignalGenerated = "SIGNAL_GENERATED";
    public const string IndicatorCalculated = "INDICATOR_CALCULATED";
    public const string MarketRegimeChanged = "MARKET_REGIME_CHANGED";
    public const string FundingRateUpdated = "FUNDING_RATE_UPDATED";

    // Strategy
    public const string StrategyEvaluated = "STRATEGY_EVALUATED";
    public const string OrderMapped = "ORDER_MAPPED";

    // RiskGuard
    public const string RiskValidationStarted = "RISK_VALIDATION_STARTED";
    public const string RiskRuleEvaluated = "RISK_RULE_EVALUATED";
    public const string RiskValidationApproved = "RISK_VALIDATION_APPROVED";
    public const string RiskValidationRejected = "RISK_VALIDATION_REJECTED";

    // Executor
    public const string OrderPlaced = "ORDER_PLACED";
    public const string OrderFilled = "ORDER_FILLED";
    public const string OrderPartiallyFilled = "ORDER_PARTIALLY_FILLED";
    public const string OrderCancelled = "ORDER_CANCELLED";
    public const string PositionOpened = "POSITION_OPENED";
    public const string PositionClosed = "POSITION_CLOSED";
    public const string TakeProfitHit = "TAKE_PROFIT_HIT";
    public const string StopLossHit = "STOP_LOSS_HIT";
    public const string PositionLiquidated = "POSITION_LIQUIDATED";
    public const string SessionClosed = "SESSION_CLOSED";

    // Notifier
    public const string NotificationSent = "NOTIFICATION_SENT";
}

public static class TimelineSeverity
{
    public const string Debug = "DEBUG";
    public const string Info = "INFO";
    public const string Warning = "WARNING";
    public const string Error = "ERROR";
}
