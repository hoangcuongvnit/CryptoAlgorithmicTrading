namespace Notifier.Worker.Services;

/// <summary>
/// Thread-safe ring buffer of the last <see cref="Capacity"/> sent notifications.
/// Tracks today's counts by category with automatic midnight reset.
/// </summary>
public sealed class NotificationHistory
{
    private const int Capacity = 100;

    private readonly NotificationRecord[] _buffer = new NotificationRecord[Capacity];
    private int _count;
    private int _head;
    private int _todayTotal;
    private readonly Dictionary<string, int> _todayByCategory = new(StringComparer.OrdinalIgnoreCase);
    private DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly object _lock = new();

    public void Add(string category, string summary)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        lock (_lock)
        {
            if (today != _today)
            {
                _today = today;
                _todayTotal = 0;
                _todayByCategory.Clear();
            }

            _todayTotal++;
            _todayByCategory[category] = (_todayByCategory.TryGetValue(category, out var c) ? c : 0) + 1;

            _buffer[_head] = new NotificationRecord(category, summary, now);
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }
    }

    public (int Total, IReadOnlyDictionary<string, int> ByCategory) GetTodayCounts()
    {
        lock (_lock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != _today) return (0, new Dictionary<string, int>());
            return (_todayTotal, new Dictionary<string, int>(_todayByCategory));
        }
    }

    /// <summary>Returns up to <see cref="Capacity"/> records newest-first.</summary>
    public IReadOnlyList<NotificationRecord> GetRecent()
    {
        lock (_lock)
        {
            if (_count == 0) return [];
            var result = new NotificationRecord[_count];
            for (var i = 0; i < _count; i++)
                result[i] = _buffer[((_head - 1 - i) + Capacity) % Capacity];
            return result;
        }
    }
}

public sealed record NotificationRecord(string Category, string Summary, DateTime TimestampUtc);
