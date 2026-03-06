namespace CryptoTrading.Shared.DTOs;

public sealed record OrderResult
{
    public string OrderId { get; init; } = string.Empty;
    public required string Symbol { get; init; }
    public required bool Success { get; init; }
    public decimal FilledPrice { get; init; }
    public decimal FilledQty { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public required DateTime Timestamp { get; init; }
    public bool IsPaperTrade { get; init; }
}
