namespace Executor.API.Configuration;

public sealed class TradingSettings
{
    public bool GlobalKillSwitch { get; set; }
    public List<string> AllowedSymbols { get; set; } = new();
    public decimal MinOrderAmount { get; set; } = 5m;
    public decimal MaxNotionalPerOrder { get; set; } = 1000m;
    public SpreadFilterSettings SpreadFilter { get; set; } = new();
    public PartialTpSettings PartialTp { get; set; } = new();
    public ConsensusPricingSettings ConsensusPricing { get; set; } = new();
    public ReconciliationSettings Reconciliation { get; set; } = new();
}

public sealed class ReconciliationSettings
{
    public bool Enabled { get; set; } = false;
    public int IntervalSeconds { get; set; } = 300;
    public decimal PositionQuantityTolerance { get; set; } = 0.00000001m;
    public decimal BalanceDriftTolerance { get; set; } = 0.01m;
    public string QuoteAsset { get; set; } = "USDT";
    public int AlertMaxSymbols { get; set; } = 10;
}

public sealed class SpreadFilterSettings
{
    /// <summary>When false, spread and slippage checks are skipped entirely.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Maximum allowed spread for BTC/ETH (as a fraction, e.g. 0.002 = 0.2%).</summary>
    public decimal BtcEthSpreadLimit { get; set; } = 0.002m;
    /// <summary>Maximum allowed spread for altcoins (as a fraction, e.g. 0.005 = 0.5%).</summary>
    public decimal AltcoinSpreadLimit { get; set; } = 0.005m;
    /// <summary>Log a warning when executed price deviates more than this from the requested price.</summary>
    public decimal SlippageTolerance { get; set; } = 0.001m;
    /// <summary>Symbols treated as majors for the tighter spread limit.</summary>
    public List<string> MajorSymbols { get; set; } = ["BTCUSDT", "ETHUSDT"];
}

public sealed class ConsensusPricingSettings
{
    /// <summary>When false, consensus check is skipped entirely.</summary>
    public bool Enabled { get; set; } = false;
    /// <summary>Maximum allowed price deviation between exchanges (e.g. 0.001 = 0.1%).</summary>
    public decimal PriceAgreementThreshold { get; set; } = 0.001m;
}

public sealed class PartialTpSettings
{
    /// <summary>Enable 2-stage partial take-profit monitor.</summary>
    public bool Enabled { get; set; } = false;
    /// <summary>Price gain % at which Stage 1 fires (default 1.5%).</summary>
    public decimal Stage1ProfitPercent { get; set; } = 1.5m;
    /// <summary>Fraction of position to close at Stage 1 (default 0.5 = 50%).</summary>
    public decimal Stage1CloseRatio { get; set; } = 0.5m;
    /// <summary>Price gain % at which Stage 2 fires (default 3.0%).</summary>
    public decimal Stage2ProfitPercent { get; set; } = 3.0m;
}

public sealed class RedisSettings
{
    public string Connection { get; set; } = "localhost:6379";
}

public sealed class BinanceSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string TestnetApiKey { get; set; } = string.Empty;
    public string TestnetApiSecret { get; set; } = string.Empty;
    public bool UseTestnet { get; set; } = true;

    public string ActiveApiKey => UseTestnet && !string.IsNullOrEmpty(TestnetApiKey)
        ? TestnetApiKey : ApiKey;
    public string ActiveApiSecret => UseTestnet && !string.IsNullOrEmpty(TestnetApiSecret)
        ? TestnetApiSecret : ApiSecret;
}
