namespace TimelineLogger.Worker.Configuration;

public sealed class TimelineSettings
{
    public int BatchSize { get; init; } = 100;
    public int BatchTimeoutMs { get; init; } = 5000;
    public int DefaultRetentionDays { get; init; } = 90;
    public int CacheTtlMinutes { get; init; } = 60;
}
