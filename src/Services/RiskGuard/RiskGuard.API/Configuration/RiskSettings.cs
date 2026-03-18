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

    /// <summary>When true, drawdown calculations include only paper-trade orders.</summary>
    public bool PaperTradingOnly { get; set; } = true;

    /// <summary>Symbols allowed to trade. Empty list means all symbols are allowed.</summary>
    public List<string> AllowedSymbols { get; set; } = [];
}
