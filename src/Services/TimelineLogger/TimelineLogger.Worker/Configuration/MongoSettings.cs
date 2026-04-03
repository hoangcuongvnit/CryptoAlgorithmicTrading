namespace TimelineLogger.Worker.Configuration;

public sealed class MongoSettings
{
    public string ConnectionString { get; init; } = "mongodb://localhost:27017";
    public string Database { get; init; } = "cryptotrading_timeline";
    public string CoinEventsCollection { get; init; } = "coin_events";
    public string EventSummaryCollection { get; init; } = "event_summary";
}
