namespace CryptoTrading.Shared.DTOs;

public sealed record SystemEvent
{
    public required SystemEventType Type { get; init; }
    public required string ServiceName { get; init; }
    public required string Message { get; init; }
    public required DateTime Timestamp { get; init; }
    public TradingErrorCode? ErrorCode { get; init; }
    public string? Symbol { get; init; }
}
