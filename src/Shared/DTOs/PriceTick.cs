namespace CryptoTrading.Shared.DTOs;

public sealed record PriceTick
{
    public required string Symbol { get; init; }
    public required decimal Price { get; init; }
    public required decimal Volume { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public required DateTime Timestamp { get; init; }
    public string Interval { get; init; } = "1m";
}
