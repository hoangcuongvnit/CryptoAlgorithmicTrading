using Binance.Net.Interfaces.Clients;
using CryptoExchange.Net.Objects.Sockets;
using CryptoTrading.Shared.DTOs;
using Ingestor.Worker.Configuration;
using Ingestor.Worker.Infrastructure;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Ingestor.Worker.Workers;

public sealed class BinanceIngestorWorker : BackgroundService
{
    private readonly IBinanceSocketClient _socketClient;
    private readonly RedisPublisher _redisPublisher;
    private readonly PriceTickWriteQueue _writeQueue;
    private readonly TradingSettings _settings;
    private readonly ILogger<BinanceIngestorWorker> _logger;
    private readonly ConcurrentDictionary<string, UpdateSubscription> _subscriptions = new();
    private bool _wsEventsHooked;

    public BinanceIngestorWorker(
        IBinanceSocketClient socketClient,
        RedisPublisher redisPublisher,
        PriceTickWriteQueue writeQueue,
        IOptions<TradingSettings> settings,
        ILogger<BinanceIngestorWorker> logger)
    {
        _socketClient = socketClient;
        _redisPublisher = redisPublisher;
        _writeQueue = writeQueue;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataIngestor service starting up...");

        // Publish startup event
        await _redisPublisher.PublishSystemEventAsync(new SystemEvent
        {
            Type = SystemEventType.ServiceStarted,
            ServiceName = "DataIngestor",
            Message = "Binance WebSocket data ingestion service started",
            Timestamp = DateTime.UtcNow
        }, stoppingToken);

        // Subscribe to all configured symbols
        foreach (var symbol in _settings.Symbols)
        {
            await SubscribeToSymbolAsync(symbol, stoppingToken);
        }

        _logger.LogInformation("DataIngestor subscribed to {Count} symbols", _settings.Symbols.Count);

        // Keep the service running
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("DataIngestor service shutting down...");
        }
    }

    private async Task SubscribeToSymbolAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Subscribing to {Symbol}...", symbol);

            // Subscribe to 24h mini ticker (price updates)
            var tickerResult = await _socketClient.SpotApi.ExchangeData.SubscribeToMiniTickerUpdatesAsync(
                symbol,
                data =>
                {
                    var tick = new PriceTick
                    {
                        Symbol = data.Data.Symbol,
                        Price = data.Data.LastPrice,
                        Volume = data.Data.QuoteVolume, // Changed from BaseVolume
                        Open = data.Data.OpenPrice,
                        High = data.Data.HighPrice,
                        Low = data.Data.LowPrice,
                        Close = data.Data.LastPrice,
                        Timestamp = data.Timestamp, // Changed from EventTime
                        Interval = "ticker"
                    };

                    _ = HandleTickAsync(tick, cancellationToken);
                },
                cancellationToken);

            if (tickerResult.Success)
            {
                _subscriptions.TryAdd($"{symbol}_ticker", tickerResult.Data);
                _logger.LogInformation("Successfully subscribed to ticker for {Symbol}", symbol);

                // Hook disconnect/reconnect on the first subscription only to avoid duplicate events
                if (!_wsEventsHooked)
                {
                    _wsEventsHooked = true;
                    tickerResult.Data.ConnectionLost += () =>
                    {
                        _logger.LogWarning("Binance WebSocket connection lost");
                        _ = _redisPublisher.PublishSystemEventAsync(new SystemEvent
                        {
                            Type = SystemEventType.ConnectionLost,
                            ServiceName = "DataIngestor",
                            Message = "Binance WebSocket disconnected. Reconnecting...",
                            Timestamp = DateTime.UtcNow
                        }, CancellationToken.None);
                    };
                    tickerResult.Data.ConnectionRestored += delay =>
                    {
                        _logger.LogInformation("Binance WebSocket restored after {Delay:F1}s", delay.TotalSeconds);
                        _ = _redisPublisher.PublishSystemEventAsync(new SystemEvent
                        {
                            Type = SystemEventType.ConnectionRestored,
                            ServiceName = "DataIngestor",
                            Message = $"Binance WebSocket reconnected (downtime: {delay.TotalSeconds:F1}s)",
                            Timestamp = DateTime.UtcNow
                        }, CancellationToken.None);
                    };
                }
            }
            else
            {
                _logger.LogError("Failed to subscribe to ticker for {Symbol}: {Error}",
                    symbol, tickerResult.Error?.Message);
            }

            // Subscribe to kline (candlestick) data
            var klineInterval = Binance.Net.Enums.KlineInterval.OneMinute;
            var klineResult = await _socketClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(
                symbol,
                klineInterval,
                data =>
                {
                    if (data.Data.Data.Final) // Only process completed candles
                    {
                        var tick = new PriceTick
                        {
                            Symbol = data.Data.Symbol,
                            Price = data.Data.Data.ClosePrice,
                            Volume = data.Data.Data.Volume,
                            Open = data.Data.Data.OpenPrice,
                            High = data.Data.Data.HighPrice,
                            Low = data.Data.Data.LowPrice,
                            Close = data.Data.Data.ClosePrice,
                            Timestamp = data.Data.Data.CloseTime,
                            Interval = _settings.KlineInterval
                        };

                        _ = HandleTickAsync(tick, cancellationToken);
                    }
                },
                cancellationToken);

            if (klineResult.Success)
            {
                _subscriptions.TryAdd($"{symbol}_kline", klineResult.Data);
                _logger.LogInformation("Successfully subscribed to klines for {Symbol}", symbol);
            }
            else
            {
                _logger.LogError("Failed to subscribe to klines for {Symbol}: {Error}",
                    symbol, klineResult.Error?.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to {Symbol}", symbol);

            await _redisPublisher.PublishSystemEventAsync(new SystemEvent
            {
                Type = SystemEventType.Error,
                ServiceName = "DataIngestor",
                Message = $"Failed to subscribe to {symbol}: {ex.Message}",
                Timestamp = DateTime.UtcNow
            }, cancellationToken);
        }
    }

    private async Task HandleTickAsync(PriceTick tick, CancellationToken cancellationToken)
    {
        await _redisPublisher.PublishPriceTickAsync(tick, cancellationToken);
        await _writeQueue.EnqueueAsync(tick, cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Unsubscribing from all WebSocket streams...");

        // Unsubscribe from all streams
        foreach (var subscription in _subscriptions.Values)
        {
            await subscription.CloseAsync();
        }

        _subscriptions.Clear();

        await _redisPublisher.PublishSystemEventAsync(new SystemEvent
        {
            Type = SystemEventType.ServiceStopped,
            ServiceName = "DataIngestor",
            Message = "Data ingestion service stopped",
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        await base.StopAsync(cancellationToken);
    }
}
