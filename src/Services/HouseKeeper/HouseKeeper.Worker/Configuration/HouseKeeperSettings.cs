namespace HouseKeeper.Worker.Configuration;

public sealed class HouseKeeperSettings
{
    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; } = true;
    public string ScheduleUtc { get; set; } = "03:15";
    public RetentionSettings Retention { get; set; } = new();
    public int BatchSize { get; set; } = 5000;
    public int MaxRunSeconds { get; set; } = 600;
}

public sealed class RetentionSettings
{
    public int OrdersDays { get; set; } = 365;
    public int FilledGapsDays { get; set; } = 60;
    public int PriceTicksMonths { get; set; } = 12;
}
