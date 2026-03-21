namespace CryptoTrading.Shared.DTOs;

public enum SessionPhase
{
    Open,
    SoftUnwind,
    LiquidationOnly,
    ForcedFlatten,
    SessionClosed
}
