namespace RiskGuard.API.Configuration;

public sealed class RiskSettings
{
    public decimal MinRiskReward { get; set; } = 2.0m;
    public decimal MaxOrderNotional { get; set; } = 1000m;
    public int CooldownSeconds { get; set; } = 30;
    public List<string> AllowedSymbols { get; set; } = new();
}
