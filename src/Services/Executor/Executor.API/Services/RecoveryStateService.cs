using CryptoTrading.Shared.DTOs;
using StackExchange.Redis;

namespace Executor.API.Services;

/// <summary>
/// Singleton that owns the system recovery state machine.
/// Publishes every state transition to Redis so RiskGuard and other services
/// can block new-position orders until recovery is complete.
/// </summary>
public sealed class RecoveryStateService
{
    private volatile int _currentStateInt = (int)SystemRecoveryState.Booting;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RecoveryStateService> _logger;

    private const string StateKey = "executor:recovery:state";
    private const string RunIdKey = "executor:recovery:run_id";
    private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(24);

    public string RecoveryRunId { get; } = Guid.NewGuid().ToString("N");

    public SystemRecoveryState CurrentState =>
        (SystemRecoveryState)_currentStateInt;

    /// <summary>True while new non-reduce-only orders must be blocked.</summary>
    public bool IsBlockingNewOrders => CurrentState
        is SystemRecoveryState.Booting
        or SystemRecoveryState.RecoveryMode
        or SystemRecoveryState.RecoveryExecuting;

    public RecoveryStateService(
        IConnectionMultiplexer redis,
        ILogger<RecoveryStateService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public void TransitionTo(SystemRecoveryState newState)
    {
        var old = CurrentState;
        _currentStateInt = (int)newState;
        _logger.LogInformation(
            "Recovery state: {Old} → {New} (runId={RunId})",
            old, newState, RecoveryRunId);

        // Fire-and-forget — we never block the calling thread on Redis
        _ = PublishStateAsync(newState);
    }

    private async Task PublishStateAsync(SystemRecoveryState state)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(StateKey, state.ToString(), KeyTtl);
            await db.StringSetAsync(RunIdKey, RecoveryRunId, KeyTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish recovery state '{State}' to Redis", state);
        }
    }
}
