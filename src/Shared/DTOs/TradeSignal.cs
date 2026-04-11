namespace CryptoTrading.Shared.DTOs;

public sealed record TradeSignal
{
    public required string Symbol { get; init; }
    public decimal Rsi { get; init; }
    public decimal Ema9 { get; init; }
    public decimal Ema21 { get; init; }
    public decimal BbUpper { get; init; }
    public decimal BbMiddle { get; init; }
    public decimal BbLower { get; init; }
    public SignalStrength Strength { get; init; }
    public required DateTime Timestamp { get; init; }

    // Phase 1: Signal quality filters
    public decimal Adx { get; init; }
    public decimal VolumeRatio { get; init; }
    public string? VolumeFlag { get; init; }

    // Phase 2.1: ATR for adaptive stop-loss
    public decimal Atr14 { get; init; }

    // Phase 3.1: Market regime classification
    public MarketRegime Regime { get; init; }

    // Phase 4.1: Bollinger Band squeeze flag
    public bool BbSqueeze { get; init; }
}
