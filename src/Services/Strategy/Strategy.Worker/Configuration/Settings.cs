namespace Strategy.Worker.Configuration;

public sealed class RedisSettings
{
    public string Connection { get; set; } = "localhost:6379";
}

public sealed class GrpcEndpoints
{
    public string RiskGuardUrl { get; set; } = "http://localhost:5013";
    public string ExecutorUrl { get; set; } = "http://localhost:5014";
}

public sealed class TradingSettings
{
    public decimal DefaultOrderQuantity { get; set; } = 0.001m;
    public SignalThreshold MinimumSignalStrength { get; set; } = SignalThreshold.Moderate;
}

public enum SignalThreshold
{
    Weak = 1,
    Moderate = 2,
    Strong = 3
}
