namespace RiskGuard.API.Services;

/// <summary>
/// Thread-safe ring buffer of the last <see cref="MaxCapacity"/> validation decisions.
/// Also tracks today's approved/rejected counters with automatic midnight reset.
/// </summary>
public sealed class ValidationHistory
{
    private const int MaxCapacity = 50;

    private readonly ValidationRecord[] _buffer = new ValidationRecord[MaxCapacity];
    private int _count;
    private int _head;
    private int _todayApproved;
    private int _todayRejected;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly object _lock = new();

    public void Record(string symbol, string side, bool approved, string rejectionReason)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

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

            _buffer[_head] = new ValidationRecord(symbol, side, approved, rejectionReason, now);
            _head = (_head + 1) % MaxCapacity;
            if (_count < MaxCapacity) _count++;
        }
    }

    public (int Approved, int Rejected) GetTodayCounts()
    {
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
}

public sealed record ValidationRecord(
    string Symbol,
    string Side,
    bool Approved,
    string RejectionReason,
    DateTime TimestampUtc);
