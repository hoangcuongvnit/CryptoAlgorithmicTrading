using CryptoTrading.Shared.DTOs;

namespace CryptoTrading.Shared.Session;

public sealed record SessionInfo(
    string SessionId,
    int SessionNumber,
    DateTime SessionStartUtc,
    DateTime SessionEndUtc,
    DateTime LiquidationStartUtc,
    SessionPhase CurrentPhase,
    TimeSpan TimeToEnd,
    TimeSpan TimeToLiquidation);
