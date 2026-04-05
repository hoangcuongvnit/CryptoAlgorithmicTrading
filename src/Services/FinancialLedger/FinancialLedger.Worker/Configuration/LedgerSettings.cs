namespace FinancialLedger.Worker.Configuration;

public sealed class LedgerSettings
{
    public int DefaultInitialBalance { get; set; } = 10000;
    public int RedisReadBlockMs { get; set; } = 3000;
    public int RedisReadBatchSize { get; set; } = 100;
    public string RedisEventsStreamKey { get; set; } = "ledger:events";
    public string RedisConsumerGroup { get; set; } = "financial-ledger";
    public string RedisConsumerName { get; set; } = Environment.MachineName;
    public string DefaultEnvironment { get; set; } = "TESTNET";
    public string DefaultAlgorithmName { get; set; } = "DEFAULT";

    // Session reset orchestration
    public int CloseAllTimeoutSeconds { get; set; } = 180;
    public int CloseAllPollIntervalMs { get; set; } = 2000;
    public string CloseAllConfirmationToken { get; set; } = "CLOSE ALL";

    // EquityProjectionWorker
    public string ExecutorUrl { get; set; } = "";
    public int EquityProjectionIntervalMs { get; set; } = 5000;
}
