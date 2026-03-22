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

    // Phase 1.1: ADX trend strength filter
    public int AdxPeriod { get; set; } = 14;
    public decimal AdxTrendThreshold { get; set; } = 25m;
    public decimal AdxStrongThreshold { get; set; } = 40m;

    // Phase 1.2: Volume confirmation
    public int VolumeAveragePeriod { get; set; } = 20;
    public decimal VolumeConfirmationRatio { get; set; } = 1.5m;
    public decimal VolumeAnomalyZscoreThreshold { get; set; } = 3.0m;

    // Phase 2.1: ATR for adaptive stop-loss
    public int AtrPeriod { get; set; } = 14;

    // Phase 2.4: Short selling safety
    public decimal ShortRsiThreshold { get; set; } = 60m;
    public decimal MaxFundingRate { get; set; } = 0.02m;

    // Phase 3.1: Market regime detection (needs wider buffer — set BufferCapacity >= 120)
    public decimal RegimeHighVolAtrMultiplier { get; set; } = 1.5m;
    /// <summary>BB width must exceed this multiple of the historical mean to flag HighVolatility regime.</summary>
    public decimal RegimeBbWidthExpansion { get; set; } = 1.2m;

    // Phase 4.1: Bollinger Band squeeze detection
    public int BbSqueezeBaselinePeriod { get; set; } = 100;
    public decimal BbSqueezeMultiplier { get; set; } = 0.8m;
}
