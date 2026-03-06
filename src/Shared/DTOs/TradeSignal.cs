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
}
