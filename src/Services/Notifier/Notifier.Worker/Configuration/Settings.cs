namespace Notifier.Worker.Configuration;

public sealed class TelegramSettings
{
    public string BotToken { get; set; } = string.Empty;
    public long ChatId { get; set; }
}

public sealed class RedisSettings
{
    public string Connection { get; set; } = "localhost:6379";
}
