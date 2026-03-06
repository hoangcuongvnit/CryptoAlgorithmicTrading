using StackExchange.Redis;
using System.Text.Json;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using CryptoTrading.Shared.Constants;
using Notifier.Worker.Channels;

namespace Notifier.Worker.Workers;

public sealed class NotifierWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly ILogger<NotifierWorker> _logger;

    public NotifierWorker(
        IConnectionMultiplexer redis,
        TelegramNotifier telegramNotifier,
        ILogger<NotifierWorker> logger)
    {
        _redis = redis;
        _telegramNotifier = telegramNotifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notifier service starting up...");

        // Test Telegram connection
        if (!await _telegramNotifier.TestConnectionAsync(stoppingToken))
        {
            _logger.LogError("Failed to connect to Telegram. Service will continue but notifications will fail.");
        }

        // Send startup notification
        await _telegramNotifier.SendStartupNotificationAsync(stoppingToken);

        var subscriber = _redis.GetSubscriber();

        // Subscribe to system events
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisChannels.SystemEvents),
            async (channel, message) =>
            {
                try
                {
                    var systemEvent = JsonSerializer.Deserialize(message.ToString(), TradingJsonContext.Default.SystemEvent);
                    if (systemEvent != null)
                    {
                        await _telegramNotifier.SendSystemEventAsync(systemEvent, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing system event notification");
                }
            });

        // Subscribe to trades audit (order results)
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisChannels.TradesAudit),
            async (channel, message) =>
            {
                try
                {
                    var orderResult = JsonSerializer.Deserialize(message.ToString(), TradingJsonContext.Default.OrderResult);
                    if (orderResult != null)
                    {
                        await _telegramNotifier.SendOrderResultAsync(orderResult, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing order result notification");
                }
            });

        _logger.LogInformation("Notifier subscribed to Redis channels: {Channels}",
            string.Join(", ", RedisChannels.SystemEvents, RedisChannels.TradesAudit));

        // Keep the service running
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Notifier service shutting down...");
        }
    }
}
