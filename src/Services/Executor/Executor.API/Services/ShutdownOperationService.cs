using Dapper;
using Npgsql;
using StackExchange.Redis;
using System.Text.Json;

namespace Executor.API.Services;

/// <summary>
/// Singleton state machine that owns the lifecycle of close-all / flatten operations.
/// Writes state to Redis (for cross-service visibility) and PostgreSQL (audit trail).
/// </summary>
public sealed class ShutdownOperationService
{
    public record OperationInfo
    {
        public Guid OperationId { get; init; }
        public string Status { get; init; } = "Idle";
        public string OperationType { get; init; } = string.Empty;
        public string RequestedBy { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public DateTime RequestedAtUtc { get; init; }
        public DateTime? ScheduledForUtc { get; init; }
        public DateTime? StartedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public bool ShutdownReady { get; init; }
        public int PositionsClosedCount { get; init; }
        public string? LastError { get; init; }
        public string IdempotencyKey { get; init; } = string.Empty;
        public int DiscoveredCandidatesCount { get; init; }
        public int AttemptedCloseCount { get; init; }
        public DateTime? VerifiedAtUtc { get; init; }
        public IReadOnlyList<CloseAllLeftoverAsset> Leftovers { get; init; } = [];
    }

    private static readonly HashSet<string> ActiveStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Requested", "Scheduled", "Executing" };

    private readonly object _lock = new();
    private OperationInfo _current = new() { Status = "Idle" };
    private string _tradingMode = "TradingEnabled";
    private string? _exitOnlySource; // "manual" | "session_end" | null
    private bool _sessionExitOnlyActive; // true when auto-set by session monitor
    private const string TradingModeRedisKey = "executor:trading:mode";
    private readonly IConnectionMultiplexer _redis;
    private readonly string _connectionString;
    private readonly ILogger<ShutdownOperationService> _logger;

    private const string RedisKey = "executor:close_all:operation";
    private static readonly TimeSpan RedisTtl = TimeSpan.FromHours(48);

    /// <summary>
    /// True while an active close-all operation is in progress.
    /// When true, the order gate blocks all non-reduce-only orders.
    /// </summary>
    public bool IsExitOnlyMode => _tradingMode == "ExitOnly";

    public string TradingMode => _tradingMode;

    /// <summary>Source of current exit-only: "manual", "session_end", or null when trading is enabled.</summary>
    public string? ExitOnlySource => _exitOnlySource;

    /// <summary>True when session monitor has set exit-only due to final 30-minute window.</summary>
    public bool IsSessionExitOnlyActive => _sessionExitOnlyActive;

    public bool ResumeAllowed
    {
        get
        {
            lock (_lock)
            {
                if (_tradingMode != "ExitOnly") return false;
                if (ActiveStatuses.Contains(_current.Status)) return false;
                if (_sessionExitOnlyActive) return false; // Cannot manually resume during session exit-only
                return true;
            }
        }
    }

    public IReadOnlyList<string> GetResumeBlockReasons()
    {
        lock (_lock)
        {
            var reasons = new List<string>();
            if (_tradingMode != "ExitOnly")
                reasons.Add("Trading is already enabled.");
            else
            {
                if (ActiveStatuses.Contains(_current.Status))
                    reasons.Add($"Active operation {_current.OperationId} is currently {_current.Status}. Wait for completion or cancel first.");
                if (_sessionExitOnlyActive)
                    reasons.Add("Session is in the final 30-minute window. Exit-only mode will be released automatically when the window ends.");
            }
            return reasons;
        }
    }

    public OperationInfo Current => _current;

    public ShutdownOperationService(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<ShutdownOperationService> logger)
    {
        _redis = redis;
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");
        _logger = logger;
    }

    /// <summary>Loads any persisted scheduled operation from Redis on startup.</summary>
    public async Task LoadFromRedisAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = await db.StringGetAsync(RedisKey);
            var modeValue = await db.StringGetAsync(TradingModeRedisKey);
            if (!modeValue.IsNullOrEmpty && (string)modeValue! == "ExitOnly")
            {
                lock (_lock) { _tradingMode = "ExitOnly"; }
            }
            if (json.IsNullOrEmpty) return;

            var loaded = JsonSerializer.Deserialize<OperationInfo>((string)json!);
            if (loaded is null) return;

            // Only restore Scheduled operations — Requested/Executing may be stale
            if (loaded.Status == "Scheduled" && loaded.ScheduledForUtc > DateTime.UtcNow)
            {
                lock (_lock)
                {
                    _current = loaded;
                }
                _logger.LogInformation(
                    "Restored scheduled close-all from Redis: operationId={OperationId} scheduledFor={ScheduledFor}",
                    loaded.OperationId, loaded.ScheduledForUtc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load close-all state from Redis on startup");
        }
    }

    public (bool Success, string? Error, Guid OperationId) RequestCloseAll(
        string reason, string requestedBy, string idempotencyKey)
    {
        lock (_lock)
        {
            // Idempotency: same key returns existing active operation
            if (_current.IdempotencyKey == idempotencyKey && ActiveStatuses.Contains(_current.Status))
                return (true, null, _current.OperationId);

            if (ActiveStatuses.Contains(_current.Status))
                return (false, $"Active operation {_current.OperationId} is already {_current.Status}. Cancel it first.", Guid.Empty);

            var operationId = Guid.NewGuid();
            _current = new OperationInfo
            {
                OperationId = operationId,
                Status = "Requested",
                OperationType = "close_all_now",
                RequestedBy = requestedBy,
                Reason = reason,
                RequestedAtUtc = DateTime.UtcNow,
                IdempotencyKey = idempotencyKey
            };

            _logger.LogInformation(
                "CloseAll requested: operationId={OperationId} by={RequestedBy} reason={Reason}",
                operationId, requestedBy, reason);

            _tradingMode = "ExitOnly";
            _exitOnlySource = "manual";
            _ = PersistTradingModeToRedisAsync("ExitOnly");
            _ = PersistToRedisAsync(_current);
            _ = InsertToDbAsync(_current);
            return (true, null, operationId);
        }
    }

    public (bool Success, string? Error, Guid OperationId) ScheduleCloseAll(
        DateTime executeAtUtc, string reason, string requestedBy, string idempotencyKey)
    {
        if (executeAtUtc <= DateTime.UtcNow.AddSeconds(30))
            return (false, "Scheduled time must be at least 30 seconds in the future.", Guid.Empty);

        lock (_lock)
        {
            if (_current.IdempotencyKey == idempotencyKey && ActiveStatuses.Contains(_current.Status))
                return (true, null, _current.OperationId);

            if (ActiveStatuses.Contains(_current.Status))
                return (false, $"Active operation {_current.OperationId} is already {_current.Status}. Cancel it first.", Guid.Empty);

            var operationId = Guid.NewGuid();
            _current = new OperationInfo
            {
                OperationId = operationId,
                Status = "Scheduled",
                OperationType = "close_all_scheduled",
                RequestedBy = requestedBy,
                Reason = reason,
                RequestedAtUtc = DateTime.UtcNow,
                ScheduledForUtc = executeAtUtc,
                IdempotencyKey = idempotencyKey
            };

            _logger.LogInformation(
                "CloseAll scheduled: operationId={OperationId} at={ScheduledFor} by={RequestedBy}",
                operationId, executeAtUtc, requestedBy);

            _tradingMode = "ExitOnly";
            _exitOnlySource = "manual";
            _ = PersistTradingModeToRedisAsync("ExitOnly");
            _ = PersistToRedisAsync(_current);
            _ = InsertToDbAsync(_current);
            return (true, null, operationId);
        }
    }

    public (bool Success, string? Error) TryCancel(Guid operationId)
    {
        lock (_lock)
        {
            if (_current.OperationId != operationId)
                return (false, $"Operation {operationId} not found.");

            if (_current.Status == "Executing")
                return (false, "Cannot cancel an operation that is already executing.");

            if (!ActiveStatuses.Contains(_current.Status))
                return (false, $"Operation {operationId} is already {_current.Status}.");

            _current = _current with { Status = "Canceled", CompletedAtUtc = DateTime.UtcNow };
            _logger.LogInformation("CloseAll canceled: operationId={OperationId}", operationId);

            _ = PersistToRedisAsync(_current);
            _ = UpdateStatusInDbAsync(_current);
            return (true, null);
        }
    }

    public (bool Success, string? Error) TryResume(string reason, string requestedBy)
    {
        lock (_lock)
        {
            if (ActiveStatuses.Contains(_current.Status))
                return (false, $"Cannot resume: active operation {_current.OperationId} is currently {_current.Status}. Cancel or wait for completion first.");

            if (_sessionExitOnlyActive)
                return (false, "Cannot resume: session is in the final 30-minute window. Exit-only mode will be released automatically when the window ends.");

            if (_tradingMode == "TradingEnabled")
                return (true, null); // Already enabled — idempotent

            _tradingMode = "TradingEnabled";
            _exitOnlySource = null;
            _logger.LogInformation(
                "Trading resumed: requestedBy={RequestedBy} reason={Reason}",
                requestedBy, reason);
            _ = PersistTradingModeToRedisAsync("TradingEnabled");
            return (true, null);
        }
    }

    /// <summary>Called by SessionExitOnlyMonitorService to enter session-based exit-only mode.</summary>
    public void EnterSessionExitOnly()
    {
        lock (_lock)
        {
            if (_sessionExitOnlyActive) return; // Already in session exit-only

            _sessionExitOnlyActive = true;

            // Only change trading mode if not already in exit-only from a manual operation
            if (_tradingMode != "ExitOnly")
            {
                _tradingMode = "ExitOnly";
                _exitOnlySource = "session_end";
                _logger.LogInformation("Session exit-only mode activated (final 30-minute window)");
                _ = PersistTradingModeToRedisAsync("ExitOnly");
            }
        }
    }

    /// <summary>Called by SessionExitOnlyMonitorService to release session-based exit-only mode.</summary>
    public void ReleaseSessionExitOnly()
    {
        lock (_lock)
        {
            if (!_sessionExitOnlyActive) return;

            _sessionExitOnlyActive = false;
            _logger.LogInformation("Session exit-only window ended");

            // Only re-enable trading if no manual operation is keeping exit-only active
            if (_exitOnlySource == "session_end" && !ActiveStatuses.Contains(_current.Status))
            {
                _tradingMode = "TradingEnabled";
                _exitOnlySource = null;
                _logger.LogInformation("Trading automatically resumed after session exit-only window ended");
                _ = PersistTradingModeToRedisAsync("TradingEnabled");
            }
        }
    }

    public void TransitionToExecuting(Guid operationId)
    {
        lock (_lock)
        {
            if (_current.OperationId != operationId) return;
            _current = _current with { Status = "Executing", StartedAtUtc = DateTime.UtcNow };
            _ = PersistToRedisAsync(_current);
            _ = UpdateStatusInDbAsync(_current);
        }
    }

    public void TransitionToCompleted(
        Guid operationId,
        int closedCount,
        int discoveredCandidatesCount = 0,
        int attemptedCloseCount = 0,
        DateTime? verifiedAtUtc = null,
        IReadOnlyList<CloseAllLeftoverAsset>? leftovers = null)
    {
        lock (_lock)
        {
            if (_current.OperationId != operationId) return;
            _current = _current with
            {
                Status = "Completed",
                CompletedAtUtc = DateTime.UtcNow,
                ShutdownReady = true,
                PositionsClosedCount = closedCount,
                DiscoveredCandidatesCount = discoveredCandidatesCount,
                AttemptedCloseCount = attemptedCloseCount,
                VerifiedAtUtc = verifiedAtUtc,
                Leftovers = leftovers ?? []
            };
            _logger.LogInformation(
                "CloseAll completed: operationId={OperationId} closedCount={Count}",
                operationId, closedCount);
            _ = PersistToRedisAsync(_current);
            _ = UpdateStatusInDbAsync(_current);
        }
    }

    public void TransitionToCompletedWithErrors(
        Guid operationId,
        int closedCount,
        string error,
        int discoveredCandidatesCount = 0,
        int attemptedCloseCount = 0,
        DateTime? verifiedAtUtc = null,
        IReadOnlyList<CloseAllLeftoverAsset>? leftovers = null)
    {
        lock (_lock)
        {
            if (_current.OperationId != operationId) return;
            _current = _current with
            {
                Status = "CompletedWithErrors",
                CompletedAtUtc = DateTime.UtcNow,
                ShutdownReady = false,
                PositionsClosedCount = closedCount,
                LastError = error,
                DiscoveredCandidatesCount = discoveredCandidatesCount,
                AttemptedCloseCount = attemptedCloseCount,
                VerifiedAtUtc = verifiedAtUtc,
                Leftovers = leftovers ?? []
            };
            _logger.LogWarning(
                "CloseAll completed with errors: operationId={OperationId} error={Error}",
                operationId, error);
            _ = PersistToRedisAsync(_current);
            _ = UpdateStatusInDbAsync(_current);
        }
    }

    public async Task<IReadOnlyList<OperationInfo>> GetHistoryAsync(int limit, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            var rows = await conn.QueryAsync("""
                SELECT operation_id, operation_type, status, requested_by, reason,
                       requested_at_utc, scheduled_for_utc, started_at_utc, completed_at_utc,
                       shutdown_ready, positions_closed_count, idempotency_key,
                       error_summary->>'error' AS last_error
                FROM trading_control_operations
                ORDER BY requested_at_utc DESC
                LIMIT @Limit
                """, new { Limit = limit });

            return rows.Select(r => new OperationInfo
            {
                OperationId = (Guid)r.operation_id,
                OperationType = (string?)r.operation_type ?? string.Empty,
                Status = (string?)r.status ?? "Idle",
                RequestedBy = (string?)r.requested_by ?? string.Empty,
                Reason = (string?)r.reason ?? string.Empty,
                RequestedAtUtc = (DateTime)r.requested_at_utc,
                ScheduledForUtc = (DateTime?)r.scheduled_for_utc,
                StartedAtUtc = (DateTime?)r.started_at_utc,
                CompletedAtUtc = (DateTime?)r.completed_at_utc,
                ShutdownReady = (bool)r.shutdown_ready,
                PositionsClosedCount = (int?)r.positions_closed_count ?? 0,
                IdempotencyKey = (string?)r.idempotency_key ?? string.Empty,
                LastError = (string?)r.last_error
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch close-all history from DB");
            return [];
        }
    }

    private async Task PersistToRedisAsync(OperationInfo info)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(info);
            await db.StringSetAsync(RedisKey, json, RedisTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist close-all state to Redis");
        }
    }

    private async Task InsertToDbAsync(OperationInfo info)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.ExecuteAsync("""
                INSERT INTO trading_control_operations
                    (operation_id, operation_type, status, requested_by, reason,
                     requested_at_utc, scheduled_for_utc, shutdown_ready, idempotency_key)
                VALUES
                    (@OperationId, @OperationType, @Status, @RequestedBy, @Reason,
                     @RequestedAtUtc, @ScheduledForUtc, @ShutdownReady, @IdempotencyKey)
                ON CONFLICT (idempotency_key) DO NOTHING
                """,
                new
                {
                    info.OperationId,
                    info.OperationType,
                    info.Status,
                    info.RequestedBy,
                    info.Reason,
                    info.RequestedAtUtc,
                    info.ScheduledForUtc,
                    info.ShutdownReady,
                    info.IdempotencyKey
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to insert close-all operation to DB: operationId={OperationId}", info.OperationId);
        }
    }

    private async Task PersistTradingModeToRedisAsync(string mode)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(TradingModeRedisKey, mode, TimeSpan.FromDays(30));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist trading mode to Redis");
        }
    }

    private async Task UpdateStatusInDbAsync(OperationInfo info)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.ExecuteAsync("""
                UPDATE trading_control_operations
                SET status                  = @Status,
                    started_at_utc         = @StartedAtUtc,
                    completed_at_utc       = @CompletedAtUtc,
                    shutdown_ready         = @ShutdownReady,
                    positions_closed_count = @PositionsClosedCount,
                    error_summary          = @ErrorSummary::jsonb
                WHERE operation_id = @OperationId
                """,
                new
                {
                    info.Status,
                    info.StartedAtUtc,
                    info.CompletedAtUtc,
                    info.ShutdownReady,
                    info.PositionsClosedCount,
                    ErrorSummary = info.LastError != null
                        ? JsonSerializer.Serialize(new { error = info.LastError })
                        : null,
                    info.OperationId
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update close-all status in DB: operationId={OperationId}", info.OperationId);
        }
    }
}
