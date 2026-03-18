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
    private readonly IConnectionMultiplexer _redis;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly NotificationHistory _history;
    private readonly ILogger<NotifierWorker> _logger;

    public NotifierWorker(
        IConnectionMultiplexer redis,
        TelegramNotifier telegramNotifier,
        NotificationHistory history,
        ILogger<NotifierWorker> logger)
    {
        _redis = redis;
        _telegramNotifier = telegramNotifier;
        _history = history;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notifier service starting up...");

        if (!await _telegramNotifier.TestConnectionAsync(stoppingToken))
            _logger.LogError("Failed to connect to Telegram. Service will continue but notifications will fail.");

        await _telegramNotifier.SendStartupNotificationAsync(stoppingToken);
        _history.Add("startup", $"CryptoTrader ONLINE | {DateTime.UtcNow:HH:mm} UTC");

        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisChannels.SystemEvents),
            async (_, message) =>
            {
                try
                {
                    var systemEvent = JsonSerializer.Deserialize(message.ToString(), TradingJsonContext.Default.SystemEvent);
                    if (systemEvent != null)
                    {
                        await _telegramNotifier.SendSystemEventAsync(systemEvent, stoppingToken);
                        _history.Add("system_event",
                            $"{systemEvent.Type}: [{systemEvent.ServiceName}] {systemEvent.Message}");
                    }
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
        var lastId = "$";

        _logger.LogInformation("Polling {Stream} Redis stream for order notifications...", RedisChannels.TradesAudit);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadAsync(RedisChannels.TradesAudit, lastId, count: 10);

                foreach (var entry in entries)
                {
                    lastId = entry.Id;
                    var orderResult = ParseStreamEntry(entry);
                    if (orderResult != null)
                    {
                        await _telegramNotifier.SendOrderResultAsync(orderResult, stoppingToken);
                        RecordOrderNotification(orderResult);
                    }
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

    private void RecordOrderNotification(OrderResult order)
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

            return new OrderResult
            {
                OrderId = fields.GetValueOrDefault("order_id", string.Empty),
                Symbol = fields.GetValueOrDefault("symbol", "UNKNOWN"),
                Side = side,
                Success = success,
                FilledPrice = filledPrice,
                FilledQty = filledQty,
                ErrorMessage = fields.GetValueOrDefault("error_message", string.Empty),
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
