namespace Notifier.Worker.Configuration;

public sealed class TelegramSettings
{
    public string BotToken { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public bool EnableBatching { get; set; } = true;
    public int BatchIntervalSeconds { get; set; } = 300;
    public int MaxBatchSize { get; set; } = 100;
}

public sealed class RedisSettings
{
    public string Connection { get; set; } = "localhost:6379";
}
