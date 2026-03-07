namespace Executor.API.Configuration;

public sealed class TradingSettings
{
    public bool PaperTradingMode { get; set; } = true;
    public bool GlobalKillSwitch { get; set; }
    public List<string> AllowedSymbols { get; set; } = new();
    public decimal MaxNotionalPerOrder { get; set; } = 1000m;
    public decimal PaperSlippageBps { get; set; } = 2m;
}

public sealed class RedisSettings
{
    public string Connection { get; set; } = "localhost:6379";
}

public sealed class BinanceSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool UseTestnet { get; set; } = true;
}
