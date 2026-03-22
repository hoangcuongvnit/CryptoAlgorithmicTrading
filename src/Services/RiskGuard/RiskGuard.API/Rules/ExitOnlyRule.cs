using CryptoTrading.Shared.DTOs;
using StackExchange.Redis;

namespace RiskGuard.API.Rules;

/// <summary>
/// Reads the Executor's trading mode from Redis and blocks new-position
/// (non-reduce-only) orders while the system is in ExitOnly mode.
///
/// This is a defence-in-depth layer: the primary gate is in Strategy.Worker
/// (which skips sending orders entirely), but RiskGuard also enforces it so
/// any order that reaches RiskGuard receives an early rejection with a clear reason.
///
/// Fail-open on Redis errors — it is safer to let the order through to Executor
/// (which has its own gate) than to permanently block all orders when Redis is down.
/// </summary>
public sealed class ExitOnlyRule : IRiskRule
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ExitOnlyRule> _logger;

    private const string TradingModeKey = "executor:trading:mode";

    public string Name => "ExitOnly";

    public ExitOnlyRule(
        IConnectionMultiplexer redis,
        ILogger<ExitOnlyRule> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        // Reduce-only orders (close-all flattens, liquidation closes) are always allowed
        if (context.IsReduceOnly)
            return RuleResult.Pass();

        try
        {
            var db = _redis.GetDatabase();
            var mode = (string?)await db.StringGetAsync(TradingModeKey).WaitAsync(ct);

            if (string.IsNullOrEmpty(mode) || mode == "TradingEnabled")
                return RuleResult.Pass();

            return RuleResult.Reject(
                $"System is in {mode} mode. New position orders are blocked until trading is resumed.",
                reasonCode: "EXIT_ONLY_MODE",
                actualValue: mode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ExitOnlyRule: Redis unavailable — failing open to avoid blocking all orders");
            return RuleResult.Pass();
        }
    }
}
