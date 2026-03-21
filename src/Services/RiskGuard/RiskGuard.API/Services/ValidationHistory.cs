using RiskGuard.API.Infrastructure;

namespace RiskGuard.API.Services;

/// <summary>
/// Thread-safe ring buffer of the last <see cref="MaxCapacity"/> validation decisions.
/// Also tracks today's approved/rejected counters with automatic midnight reset.
/// </summary>
public sealed class ValidationHistory
{
    private const int MaxCapacity = 50;

    private readonly IRedisPersistenceService _redis;
    private readonly ILogger<ValidationHistory> _logger;
    private readonly Task _initializeTask;
    private readonly ValidationRecord[] _buffer = new ValidationRecord[MaxCapacity];
    private int _count;
    private int _head;
    private int _todayApproved;
    private int _todayRejected;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly object _lock = new();

    public ValidationHistory(IRedisPersistenceService redis, ILogger<ValidationHistory> logger)
    {
        _redis = redis;
        _logger = logger;
        _initializeTask = InitializeFromRedisAsync();
    }

    public void Record(string symbol, string side, bool approved, string rejectionReason)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        ValidationRecord record;
        var approvedCount = 0;
        var rejectedCount = 0;

        lock (_lock)
        {
            if (today != _today)
            {
                _today = today;
                _todayApproved = 0;
                _todayRejected = 0;
            }

            if (approved) _todayApproved++;
            else _todayRejected++;

            record = new ValidationRecord(symbol, side, approved, rejectionReason, now);
            AddToBufferUnsafe(record);
            approvedCount = _todayApproved;
            rejectedCount = _todayRejected;
        }

        _ = PersistValidationAsync(record);

        if (approvedCount % 5 == 0 || rejectedCount % 5 == 0)
            _ = PersistTodayCountsAsync(approvedCount, rejectedCount);
    }

    public (int Approved, int Rejected) GetTodayCounts()
    {
        EnsureInitializedStarted();

        lock (_lock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != _today) return (0, 0);
            return (_todayApproved, _todayRejected);
        }
    }

    /// <summary>Returns up to <see cref="MaxCapacity"/> records newest-first.</summary>
    public IReadOnlyList<ValidationRecord> GetRecent()
    {
        EnsureInitializedStarted();

        lock (_lock)
        {
            if (_count == 0) return [];

            var result = new ValidationRecord[_count];
            for (var i = 0; i < _count; i++)
            {
                var index = ((_head - 1 - i) + MaxCapacity) % MaxCapacity;
                result[i] = _buffer[index];
            }
            return result;
        }
    }

    private async Task InitializeFromRedisAsync()
    {
        try
        {
            var records = await _redis.LoadTodayValidationsAsync(CancellationToken.None);
            var (approved, rejected) = await _redis.LoadTodayCountsAsync(CancellationToken.None);

            lock (_lock)
            {
                _today = DateOnly.FromDateTime(DateTime.UtcNow);
                _todayApproved = approved;
                _todayRejected = rejected;

                foreach (var record in records.OrderBy(r => r.TimestampUtc))
                    AddToBufferUnsafe(record);
            }

            _logger.LogInformation(
                "Loaded {Count} validation records from Redis (today: {Approved} approved, {Rejected} rejected)",
                records.Count, approved, rejected);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize validation history from Redis; starting fresh");
        }
    }

    private void AddToBufferUnsafe(ValidationRecord record)
    {
        _buffer[_head] = record;
        _head = (_head + 1) % MaxCapacity;
        if (_count < MaxCapacity) _count++;
    }

    private async Task PersistValidationAsync(ValidationRecord record)
    {
        try
        {
            await _redis.AddValidationAsync(record, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist validation record for {Symbol}", record.Symbol);
        }
    }

    private async Task PersistTodayCountsAsync(int approved, int rejected)
    {
        try
        {
            await _redis.UpdateTodayCountsAsync(approved, rejected, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update validation counters in Redis");
        }
    }

    private void EnsureInitializedStarted()
    {
        if (_initializeTask.IsFaulted)
            _ = _initializeTask.Exception;
    }
}

public sealed record ValidationRecord(
    string Symbol,
    string Side,
    bool Approved,
    string RejectionReason,
    DateTime TimestampUtc);
