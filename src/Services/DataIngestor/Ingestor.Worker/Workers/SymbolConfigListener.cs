using CryptoTrading.Shared.Constants;
using Ingestor.Worker.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Ingestor.Worker.Workers;

public sealed class SymbolConfigListener : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IOptions<TradingSettings> _settings;
    private readonly ILogger<SymbolConfigListener> _logger;

    public SymbolConfigListener(
        IConnectionMultiplexer redis,
        IOptions<TradingSettings> settings,
        ILogger<SymbolConfigListener> logger)
    {
        _redis = redis;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SymbolConfigListener starting...");

        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisChannels.ConfigSymbols),
            (channel, message) =>
            {
                try
                {
                    var newSymbols = JsonSerializer.Deserialize<List<string>>(message.ToString());
                    if (newSymbols != null)
                    {
                        _logger.LogInformation("Received symbol configuration update: {Count} symbols", newSymbols.Count);

                        // In a full implementation, this would trigger re-subscription
                        // For now, just log it
                        // TODO: Implement dynamic subscription management
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing symbol configuration update");
                }
            });

        _logger.LogInformation("SymbolConfigListener subscribed to configuration channel");

        // Keep the service running
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("SymbolConfigListener shutting down...");
        }
    }
}
