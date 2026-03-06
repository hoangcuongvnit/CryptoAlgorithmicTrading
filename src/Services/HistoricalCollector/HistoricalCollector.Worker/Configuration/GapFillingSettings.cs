namespace HistoricalCollector.Worker.Configuration;

public sealed class GapFillingSettings
{
    public bool Enabled { get; set; } = true;
    public string DailyCheckUtc { get; set; } = "02:00";
    public int ExpectedCandlesPerDay { get; set; } = 1440;
}
