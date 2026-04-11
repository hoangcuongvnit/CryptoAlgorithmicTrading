namespace FinancialLedger.Worker.Configuration;

public sealed class LedgerSettings
{
    public int DefaultInitialBalance { get; set; } = 100;
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

    // Gateway internal endpoint (source of truth for credentials from Settings UI/DB)
    public string GatewayUrl { get; set; } = "http://localhost:5000";
    public int GatewayTimeoutSeconds { get; set; } = 10;

    // Binance account widget source (owned by FinancialLedger)
    public BinanceAccountSettings Binance { get; set; } = new();
}

public sealed class BinanceAccountSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string TestnetApiKey { get; set; } = string.Empty;
    public string TestnetApiSecret { get; set; } = string.Empty;
    public bool UseTestnet { get; set; } = true;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;

    public string ActiveApiKey => UseTestnet && !string.IsNullOrWhiteSpace(TestnetApiKey)
        ? TestnetApiKey
        : ApiKey;

    public string ActiveApiSecret => UseTestnet && !string.IsNullOrWhiteSpace(TestnetApiSecret)
        ? TestnetApiSecret
        : ApiSecret;
}
