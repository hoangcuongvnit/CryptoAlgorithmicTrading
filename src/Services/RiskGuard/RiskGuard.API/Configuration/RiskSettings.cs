namespace RiskGuard.API.Configuration;

public sealed class RiskSettings
{
    /// <summary>Minimum required risk/reward ratio (e.g. 2.0 = 1:2).</summary>
    public decimal MinRiskReward { get; set; } = 2.0m;

    /// <summary>Minimum order notional in USDT. Orders below this value are rejected.</summary>
    public decimal MinOrderNotional { get; set; } = 5.0m;

    /// <summary>Maximum order notional in USDT. Orders above this value have their quantity trimmed down.</summary>
    public decimal MaxOrderNotional { get; set; } = 200.0m;

    /// <summary>Virtual account size in USDT used for position-size and drawdown calculations.</summary>
    public decimal VirtualAccountBalance { get; set; } = 100m;

    /// <summary>
    /// Temporary compatibility switch during migration. When true and live balance lookup fails,
    /// rules fall back to <see cref="VirtualAccountBalance"/>.
    /// </summary>
    public bool AllowVirtualBalanceFallback { get; set; } = true;

    /// <summary>Cache TTL for effective balance lookups (seconds).</summary>
    public int BalanceCacheTtlSeconds { get; set; } = 15;

    /// <summary>Timeout budget for outbound effective balance lookups (milliseconds).</summary>
    public int BalanceLookupTimeoutMs { get; set; } = 1200;

    /// <summary>Base currency used when querying FinancialLedger effective balance endpoint.</summary>
    public string BalanceBaseCurrency { get; set; } = "USDT";

    /// <summary>Maximum allowed daily loss as a percentage of <see cref="VirtualAccountBalance"/> (e.g. 5.0 = 5%).</summary>
    public decimal MaxDrawdownPercent { get; set; } = 5.0m;

    /// <summary>Minimum seconds between consecutive orders on the same symbol.</summary>
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>Symbols allowed to trade. Empty list means all symbols are allowed.</summary>
    public List<string> AllowedSymbols { get; set; } = [];

    // Phase 2.3: Kelly Criterion position sizing
    public bool KellyCriterionEnabled { get; set; } = false;
    /// <summary>Fraction of full Kelly to use (0.25 = quarter-Kelly).</summary>
    public decimal KellyFraction { get; set; } = 0.25m;
    /// <summary>Lookback window in days for win-rate calculation.</summary>
    public int KellyLookbackDays { get; set; } = 30;
    /// <summary>Minimum trades required before Kelly kicks in; fallback to fixed cap below this.</summary>
    public int KellyMinTrades { get; set; } = 20;

    // Phase 3.1: Market regime position-size multiplier
    public decimal HighVolPositionSizeMultiplier { get; set; } = 0.35m;
    public string RegimeRedisKeyPrefix { get; set; } = "market:regime:";
}
