namespace CryptoTrading.Shared.Session;

public sealed class SessionSettings
{
    public bool Enabled { get; set; } = true;
    public int SessionHours { get; set; } = 8;
    public int LiquidationWindowMinutes { get; set; } = 30;
    public int SoftUnwindMinutes { get; set; } = 15;
    public int ForcedFlattenMinutes { get; set; } = 2;
    public string SessionTimeZone { get; set; } = "UTC";
    public int SessionOpenCooldownSeconds { get; set; }
    public int MaxOpenPositionsPerSession { get; set; } = 5;
}
