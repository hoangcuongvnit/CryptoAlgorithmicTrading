using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using Notifier.Worker.Channels;
using Notifier.Worker.Services;
using StackExchange.Redis;
using System.Text.Json;

namespace Notifier.Worker.Workers;

public sealed class NotifierWorker : BackgroundService
{
    // System event types that must be delivered immediately regardless of batching
    private static readonly HashSet<SystemEventType> CriticalEventTypes =
    [
        SystemEventType.MaxDrawdownBreached,
        SystemEventType.Error,
        SystemEventType.ConnectionLost,
        SystemEventType.LiquidationStarted,
        SystemEventType.ForcedFlatten,
        SystemEventType.SessionNotFlat,
        SystemEventType.OrderRejected,
    ];

    private readonly IConnectionMultiplexer _redis;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly NotificationBatcher _batcher;
    private readonly NotificationHistory _history;
    private readonly TimezoneService _tz;
    private readonly ILogger<NotifierWorker> _logger;

    public NotifierWorker(
        IConnectionMultiplexer redis,
        TelegramNotifier telegramNotifier,
        NotificationBatcher batcher,
        NotificationHistory history,
        TimezoneService tz,
        ILogger<NotifierWorker> logger)
    {
        _redis = redis;
        _telegramNotifier = telegramNotifier;
        _batcher = batcher;
        _history = history;
        _tz = tz;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notifier service starting up...");

        if (!await _telegramNotifier.TestConnectionAsync(stoppingToken))
            _logger.LogError("Failed to connect to Telegram. Service will continue but notifications will fail.");

        // Startup notification is always immediate — it signals the system is live
        await _telegramNotifier.SendStartupNotificationAsync(stoppingToken);
        _history.Add("startup", $"CryptoTrader ONLINE | {_tz.Format(DateTime.UtcNow)}");

        var subscriber = _redis.GetSubscriber();

        // Subscribe to system timezone changes so message timestamps stay current
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisChannels.SystemConfigChanged),
            (_, message) =>
            {
                var ianaId = message.ToString();
                if (!string.IsNullOrWhiteSpace(ianaId))
                {
                    _tz.Update(ianaId);
                    _logger.LogInformation("Timezone updated to {Timezone} via Redis pub/sub", ianaId);
                }
            });

        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisChannels.SystemEvents),
            async (_, message) =>
            {
                try
                {
                    var systemEvent = JsonSerializer.Deserialize(message.ToString(), TradingJsonContext.Default.SystemEvent);
                    if (systemEvent is null) return;

                    var formatted = _telegramNotifier.FormatSystemEvent(systemEvent);
                    var category = "system_event";

                    if (CriticalEventTypes.Contains(systemEvent.Type))
                        await _batcher.SendCriticalAsync(formatted, stoppingToken);
                    else
                        _batcher.Enqueue(category, formatted);

                    _history.Add(category, $"{systemEvent.Type}: [{systemEvent.ServiceName}] {systemEvent.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing system event notification");
                }
            });

        _logger.LogInformation("Notifier subscribed to {Channel}", RedisChannels.SystemEvents);

        await PollTradesAuditStreamAsync(stoppingToken);
    }

    private async Task PollTradesAuditStreamAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();

        string lastId;
        try
        {
            var latest = await db.StreamRangeAsync(
                RedisChannels.TradesAudit, minId: "-", maxId: "+", count: 1,
                messageOrder: StackExchange.Redis.Order.Descending);
            lastId = latest.Length > 0 ? latest[0].Id.ToString() : "0-0";
        }
        catch
        {
            lastId = "0-0";
        }

        _logger.LogInformation("Polling {Stream} Redis stream for order notifications...", RedisChannels.TradesAudit);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadAsync(RedisChannels.TradesAudit, lastId, count: 10);

                foreach (var entry in entries)
                {
                    lastId = entry.Id.ToString();
                    var orderResult = ParseStreamEntry(entry);
                    if (orderResult is null) continue;

                    var formatted = _telegramNotifier.FormatOrderResult(orderResult);

                    if (!orderResult.Success)
                        await _batcher.SendCriticalAsync(formatted, stoppingToken);
                    else
                        _batcher.Enqueue("order", formatted);

                    RecordOrderHistory(orderResult);
                }

                await Task.Delay(500, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading {Stream} stream — retrying in 5s", RedisChannels.TradesAudit);
                await Task.Delay(5_000, stoppingToken);
            }
        }

        _logger.LogInformation("Notifier service shutting down.");
    }

    private void RecordOrderHistory(OrderResult order)
    {
        if (order.Success)
        {
            var mode = order.IsPaperTrade ? "PAPER" : "LIVE";
            var side = order.Side.ToString().ToUpperInvariant();
            _history.Add("order", $"{mode} {side} {order.Symbol} @ {order.FilledPrice:F2} | qty {order.FilledQty:F6}");
        }
        else
        {
            _history.Add("order_rejected", $"REJECTED {order.Symbol}: {order.ErrorMessage}");
        }
    }

    private OrderResult? ParseStreamEntry(StreamEntry entry)
    {
        try
        {
            var fields = entry.Values.ToDictionary(
                v => v.Name.ToString(),
                v => v.Value.ToString());

            _ = Enum.TryParse<OrderSide>(fields.GetValueOrDefault("side", "Buy"), ignoreCase: true, out var side);
            _ = bool.TryParse(fields.GetValueOrDefault("is_paper", "true"), out var isPaper);
            _ = bool.TryParse(fields.GetValueOrDefault("success", "false"), out var success);
            _ = decimal.TryParse(fields.GetValueOrDefault("filled_price", "0"), out var filledPrice);
            _ = decimal.TryParse(fields.GetValueOrDefault("filled_qty", "0"), out var filledQty);
            _ = DateTime.TryParse(fields.GetValueOrDefault("time"), out var timestamp);
            _ = Enum.TryParse<TradingErrorCode>(fields.GetValueOrDefault("error_code", "None"), ignoreCase: true, out var errorCode);

            return new OrderResult
            {
                OrderId = fields.GetValueOrDefault("order_id", string.Empty),
                Symbol = fields.GetValueOrDefault("symbol", "UNKNOWN"),
                Side = side,
                Success = success,
                FilledPrice = filledPrice,
                FilledQty = filledQty,
                ErrorMessage = fields.GetValueOrDefault("error_message", string.Empty),
                ErrorCode = errorCode,
                Timestamp = timestamp == default ? DateTime.UtcNow : timestamp,
                IsPaperTrade = isPaper
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing stream entry {Id}", entry.Id);
            return null;
        }
    }
}
