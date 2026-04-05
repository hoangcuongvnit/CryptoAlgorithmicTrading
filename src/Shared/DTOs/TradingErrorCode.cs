namespace CryptoTrading.Shared.DTOs;

public enum TradingErrorCode
{
    None = 0,

    // Exchange / network errors (1xxx)
    ExchangeRequestFailed = 1001,
    ExchangeTimeout       = 1002,
    ExchangeCircuitOpen   = 1003,

    // Pre-flight gates in Executor (2xxx)
    SpreadLimitExceeded   = 2001,
    PriceConsensusFailure = 2002,
    NoReferencePrice      = 2003,

    // Risk / policy gates (3xxx)
    RiskGuardRejected       = 3001,
    MaxDrawdownBreached     = 3002,
    MaxNotionalExceeded     = 3003,
    SymbolNotAllowed        = 3004,
    GlobalKillSwitchActive  = 3005,
    InvalidOrderParameters  = 3006,
    StaleSession            = 3007,
    SessionPhaseBlocked     = 3008,
    RecoveryModeBlocked     = 3009,
    ShutdownExitOnlyMode    = 3010,
    InsufficientPositionQuantity = 3011,
    InsufficientCashBalance = 3012,
    CashBalanceSnapshotUnavailable = 3013,

    // gRPC inter-service errors (4xxx)
    RiskGuardUnavailable  = 4001,
    ExecutorUnavailable   = 4002,
    RiskGuardCallTimeout  = 4003,
    ExecutorCallTimeout   = 4004,

    // Persistence errors (5xxx)
    DbPersistenceFailed   = 5001,
    AuditStreamFailed     = 5002,

    // Generic (9xxx)
    UnknownError = 9999
}
