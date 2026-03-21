using CryptoTrading.Shared.DTOs;
using StackExchange.Redis;

namespace RiskGuard.API.Rules;

/// <summary>
/// Reads the Executor's recovery state from Redis and blocks new-position
/// (non-reduce-only) orders while the system is in any pre-TradingEnabled state.
///
/// This is a defence-in-depth layer: the primary gate is in Executor.API's gRPC
/// service, but RiskGuard also enforces it so strategy workers receive an early
/// rejection with a meaningful reason before reaching Executor.
///
/// Fail-open on Redis errors — it is safer to let the order through to Executor
/// (which has its own gate) than to permanently block all orders when Redis is down.
/// </summary>
public sealed class RecoveryWindowRule : IRiskRule
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RecoveryWindowRule> _logger;

    private const string StateKey = "executor:recovery:state";

    public string Name => "RecoveryWindow";

    public RecoveryWindowRule(
        IConnectionMultiplexer redis,
        ILogger<RecoveryWindowRule> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        // Reduce-only orders (recovery closes, liquidation closes) are always allowed
        if (context.IsReduceOnly)
            return RuleResult.Pass();

        try
        {
            var db = _redis.GetDatabase();
            var stateValue = await db.StringGetAsync(StateKey).WaitAsync(ct);

            if (stateValue.IsNullOrEmpty)
                return RuleResult.Pass(); // Key absent means TradingEnabled (normal run)

            if (!Enum.TryParse<SystemRecoveryState>((string?)stateValue, out var state))
                return RuleResult.Pass();

            if (state is SystemRecoveryState.TradingEnabled or SystemRecoveryState.RecoveryVerified)
                return RuleResult.Pass();

            return RuleResult.Reject(
                $"System is in {state} mode. New position orders blocked until startup reconciliation completes.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RecoveryWindowRule: Redis unavailable — failing open to avoid blocking all orders");
            return RuleResult.Pass();
        }
    }
}
