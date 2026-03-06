namespace HistoricalCollector.Worker.Configuration;

public sealed class HistoricalDataSettings
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://data.binance.vision";
    public DateTime StartDate { get; set; } = new DateTime(2025, 3, 1);
    public DateTime EndDate { get; set; } = DateTime.UtcNow.Date;
    public string Interval { get; set; } = "1m";
    public List<string> Symbols { get; set; } = new();
    public int BatchSize { get; set; } = 10_000;
    public string DownloadPath { get; set; } = "./downloads";
}
