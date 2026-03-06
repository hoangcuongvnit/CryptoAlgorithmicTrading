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
    Error
}
