using Microsoft.Extensions.Options;
using Notifier.Worker.Channels;
using Notifier.Worker.Configuration;
using System.Collections.Concurrent;
using System.Text;

namespace Notifier.Worker.Services;

/// <summary>
/// Aggregates informational notifications into 5-minute batch messages while
/// passing critical notifications through immediately.
/// </summary>
public sealed class NotificationBatcher : BackgroundService
{
    private readonly ConcurrentQueue<BatchItem> _queue = new();
    private readonly TelegramNotifier _telegramNotifier;
    private readonly NotificationHistory _history;
    private readonly ILogger<NotificationBatcher> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _interval;
    private readonly int _maxBatchSize;

    public NotificationBatcher(
        TelegramNotifier telegramNotifier,
        NotificationHistory history,
        IOptions<TelegramSettings> settings,
        ILogger<NotificationBatcher> logger)
    {
        _telegramNotifier = telegramNotifier;
        _history = history;
        _logger = logger;
        _enabled = settings.Value.EnableBatching;
        _interval = TimeSpan.FromSeconds(settings.Value.BatchIntervalSeconds);
        _maxBatchSize = settings.Value.MaxBatchSize;
    }

    /// <summary>Sends a message immediately, bypassing the batch queue.</summary>
    public Task SendCriticalAsync(string message, string category = "critical", CancellationToken ct = default)
    {
        _history.Add(category, message);
        return _telegramNotifier.SendDirectMessageAsync(message, category, ct);
    }

    /// <summary>Adds an informational message to the batch queue.
    /// If batching is disabled, sends immediately.</summary>
    public void Enqueue(string category, string message)
    {
        if (!_enabled)
        {
            _history.Add(category, message);
            _ = _telegramNotifier.SendDirectMessageAsync(message, category);
            return;
        }

        _queue.Enqueue(new BatchItem(category, message));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Notification batching disabled — informational messages sent immediately.");
            return;
        }

        _logger.LogInformation("Notification batcher started. Interval={Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
                await FlushAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during notification batch flush");
            }
        }

        // Final flush on graceful shutdown so nothing is lost
        if (!_queue.IsEmpty)
            await FlushAsync(CancellationToken.None);
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        var items = new List<BatchItem>(_maxBatchSize);
        while (items.Count < _maxBatchSize && _queue.TryDequeue(out var item))
            items.Add(item);

        if (items.Count == 0) return;

        var message = FormatBatch(items);
        _history.Add("batch", $"Sent {items.Count} batched notifications");
        await _telegramNotifier.SendDirectMessageAsync(message, "batch", ct);
        _logger.LogInformation("Sent batch of {Count} notifications", items.Count);
    }

    private string FormatBatch(List<BatchItem> items)
    {
        var intervalMinutes = (int)_interval.TotalMinutes;
        var sb = new StringBuilder();
        sb.AppendLine($"📊 Trading Activity (Last {intervalMinutes} min) — {items.Count} updates");
        sb.AppendLine();

        foreach (var group in items.GroupBy(x => x.Category))
        {
            sb.AppendLine($"▸ {CategoryLabel(group.Key)}");
            foreach (var item in group)
                sb.AppendLine($"  • {item.Message}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string CategoryLabel(string category) => category switch
    {
        "order" => "Orders Filled",
        "system_event" => "System Events",
        "startup" => "Service Events",
        _ => category
    };

    private sealed record BatchItem(string Category, string Message);
}
