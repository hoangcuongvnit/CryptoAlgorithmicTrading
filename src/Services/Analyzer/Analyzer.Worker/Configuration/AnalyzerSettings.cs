namespace Analyzer.Worker.Configuration;

public sealed class AnalyzerSettings
{
    public int RsiPeriod { get; set; } = 14;
    public int EmaShortPeriod { get; set; } = 9;
    public int EmaLongPeriod { get; set; } = 21;
    public int BbPeriod { get; set; } = 20;
    public double BbStdDev { get; set; } = 2.0;
    public decimal RsiOversoldThreshold { get; set; } = 35m;
    public decimal RsiOverboughtThreshold { get; set; } = 65m;
    public int BufferCapacity { get; set; } = 60;

    /// <summary>
    /// Only process ticks from candles with this interval (e.g. "1m").
    /// Ticker updates (interval="ticker") are ignored.
    /// </summary>
    public string SignalInterval { get; set; } = "1m";
}
