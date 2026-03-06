namespace Ingestor.Worker.Configuration;

public sealed class TradingSettings
{
    public List<string> Symbols { get; set; } = new();
    public string KlineInterval { get; set; } = "1m";
}

public sealed class RedisSettings
{
    public string Connection { get; set; } = "localhost:6379";
}

public sealed class PersistenceSettings
{
    public int BufferCapacity { get; set; } = 50_000;
    public int BatchSize { get; set; } = 1_000;
    public int FlushIntervalSeconds { get; set; } = 5;
}
