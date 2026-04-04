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
    public decimal DefaultOrderNotionalUsdt { get; set; } = 25m;
    public decimal MinOrderNotionalUsdt { get; set; } = 5m;
    public SignalThreshold MinimumSignalStrength { get; set; } = SignalThreshold.Moderate;

    // Phase 2.1: Adaptive stop-loss using ATR
    public bool AdaptiveStopLossEnabled { get; set; } = false;
    public decimal AtrSlMultiplier { get; set; } = 1.5m;

    // Phase 2.2: Partial take-profit is managed by the Executor monitor (no mapper change needed)
}

public enum SignalThreshold
{
    Weak = 1,
    Moderate = 2,
    Strong = 3
}
