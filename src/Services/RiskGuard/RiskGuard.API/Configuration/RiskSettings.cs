namespace RiskGuard.API.Configuration;

public sealed class RiskSettings
{
    /// <summary>Minimum required risk/reward ratio (e.g. 2.0 = 1:2).</summary>
    public decimal MinRiskReward { get; set; } = 2.0m;

    /// <summary>Hard cap on order notional in USDT. Superseded by <see cref="MaxPositionSizePercent"/> when set.</summary>
    public decimal MaxOrderNotional { get; set; } = 1000m;

    /// <summary>Maximum single-order size as a percentage of <see cref="VirtualAccountBalance"/> (e.g. 2.0 = 2%).</summary>
    public decimal MaxPositionSizePercent { get; set; } = 2.0m;

    /// <summary>Virtual account size in USDT used for position-size and drawdown calculations.</summary>
    public decimal VirtualAccountBalance { get; set; } = 10_000m;

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
