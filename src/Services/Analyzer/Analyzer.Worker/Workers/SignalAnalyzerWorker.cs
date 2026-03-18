using Analyzer.Worker.Analysis;
using Analyzer.Worker.Configuration;
using Analyzer.Worker.Infrastructure;
using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Analyzer.Worker.Workers;

public sealed class SignalAnalyzerWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly PriceBuffer _priceBuffer;
    private readonly IndicatorEngine _indicatorEngine;
    private readonly SignalPublisher _signalPublisher;
    private readonly string _signalInterval;
    private readonly ILogger<SignalAnalyzerWorker> _logger;

    public SignalAnalyzerWorker(
        IConnectionMultiplexer redis,
        PriceBuffer priceBuffer,
        IndicatorEngine indicatorEngine,
        SignalPublisher signalPublisher,
        IOptions<AnalyzerSettings> settings,
        ILogger<SignalAnalyzerWorker> logger)
    {
        _redis = redis;
        _priceBuffer = priceBuffer;
        _indicatorEngine = indicatorEngine;
        _signalPublisher = signalPublisher;
        _signalInterval = settings.Value.SignalInterval;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SignalAnalyzer starting. Subscribing to {Pattern}...", RedisChannels.PricePattern);

        var subscriber = _redis.GetSubscriber();

        await subscriber.SubscribeAsync(
            RedisChannel.Pattern(RedisChannels.PricePattern),
            async (_, message) =>
            {
                try
                {
                    var tick = JsonSerializer.Deserialize(message.ToString(), TradingJsonContext.Default.PriceTick);
                    if (tick != null)
                        await ProcessTickAsync(tick, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing price tick");
                }
            });

        _logger.LogInformation("SignalAnalyzer subscribed and running.");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("SignalAnalyzer shutting down.");
        }
    }

    private async Task ProcessTickAsync(PriceTick tick, CancellationToken cancellationToken)
    {
        // Only process completed candles of the configured interval; skip live ticker updates
        if (!string.Equals(tick.Interval, _signalInterval, StringComparison.OrdinalIgnoreCase))
            return;

        _priceBuffer.Add(tick);

        var signal = _indicatorEngine.TryComputeSignal(tick.Symbol);
        if (signal == null)
        {
            _logger.LogTrace("Not enough data for {Symbol} yet ({Count}/{Capacity})",
                tick.Symbol, _priceBuffer.Count(tick.Symbol), _priceBuffer.Capacity);
            return;
        }

        await _signalPublisher.PublishAsync(signal, cancellationToken);
    }
}
