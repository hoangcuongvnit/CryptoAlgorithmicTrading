namespace CryptoTrading.Shared.DTOs;

public enum SystemRecoveryState
{
    Booting,
    RecoveryMode,
    RecoveryExecuting,
    RecoveryVerified,
    TradingEnabled
}

public sealed record RecoverySnapshot(
    string RecoveryRunId,
    string SessionId,
    SystemRecoveryState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int MismatchesFound,
    int ForcedClosesExecuted,
    string Source);
