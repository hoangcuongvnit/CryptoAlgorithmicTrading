namespace CryptoTrading.Shared.DTOs;

public enum SystemEventType
{
    ServiceStarted,
    ServiceStopped,
    ConnectionLost,
    ConnectionRestored,
    OrderPlaced,
    OrderRejected,
    MaxDrawdownBreached,
    Error,
    SessionStarted,
    SessionEnding,
    LiquidationStarted,
    ForcedFlatten,
    SessionFlat,
    SessionNotFlat,
    ReconciliationDrift
}
