using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.Timeline;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using StackExchange.Redis;
using System.Text.Json;
using TimelineLogger.Worker.Configuration;
using TimelineLogger.Worker.Infrastructure;
using TimelineLogger.Worker.Infrastructure.Documents;

namespace TimelineLogger.Worker.Workers;

public sealed class CoinEventLoggerWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly CoinEventRepository _repository;
    private readonly TimelineSettings _settings;
    private readonly ILogger<CoinEventLoggerWorker> _logger;

    // Category map for known event types
    private static readonly Dictionary<string, string> CategoryMap = new()
    {
        [TimelineEventTypes.PriceTickReceived] = "PRICE_DATA",
        [TimelineEventTypes.SignalGenerated] = "TRADING_SIGNAL",
        [TimelineEventTypes.IndicatorCalculated] = "ANALYSIS",
        [TimelineEventTypes.MarketRegimeChanged] = "MARKET",
        [TimelineEventTypes.StrategyEvaluated] = "STRATEGY",
        [TimelineEventTypes.OrderMapped] = "STRATEGY",
        [TimelineEventTypes.RiskValidationStarted] = "RISK",
        [TimelineEventTypes.RiskRuleEvaluated] = "RISK",
        [TimelineEventTypes.RiskValidationApproved] = "RISK",
        [TimelineEventTypes.RiskValidationRejected] = "RISK",
        [TimelineEventTypes.OrderPlaced] = "TRADING",
        [TimelineEventTypes.OrderFilled] = "TRADING",
        [TimelineEventTypes.OrderPartiallyFilled] = "TRADING",
        [TimelineEventTypes.OrderCancelled] = "TRADING",
        [TimelineEventTypes.PositionOpened] = "POSITION",
        [TimelineEventTypes.PositionClosed] = "POSITION",
        [TimelineEventTypes.TakeProfitHit] = "POSITION",
        [TimelineEventTypes.StopLossHit] = "POSITION",
        [TimelineEventTypes.PositionLiquidated] = "LIQUIDATION",
        [TimelineEventTypes.SessionClosed] = "SESSION",
        [TimelineEventTypes.NotificationSent] = "NOTIFICATION",
    };

    // Retention days per category
    private static readonly Dictionary<string, int> RetentionDays = new()
    {
        ["PRICE_DATA"] = 7,
        ["ANALYSIS"] = 30,
        ["TRADING_SIGNAL"] = 90,
        ["STRATEGY"] = 90,
        ["RISK"] = 90,
        ["NOTIFICATION"] = 90,
        ["MARKET"] = 90,
        ["TRADING"] = 365,
        ["POSITION"] = 365,
        ["LIQUIDATION"] = 365,
        ["SESSION"] = 365,
    };

    private readonly List<CoinEventDocument> _buffer = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _processedCount;
    private long _errorCount;
    private DateTime _lastFlush = DateTime.UtcNow;

    public long ProcessedCount => _processedCount;
    public long ErrorCount => _errorCount;
    public int QueueSize => _buffer.Count;

    public CoinEventLoggerWorker(
        IConnectionMultiplexer redis,
        CoinEventRepository repository,
        IOptions<TimelineSettings> settings,
        ILogger<CoinEventLoggerWorker> logger)
    {
        _redis = redis;
        _repository = repository;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CoinEventLoggerWorker starting. Subscribing to {Pattern}...", RedisChannels.TimelinePattern);

        var subscriber = _redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Pattern(RedisChannels.TimelinePattern),
            (_, message) => OnMessage(message.ToString()));

        _logger.LogInformation("CoinEventLoggerWorker subscribed and running.");

        // Periodic flush loop
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.BatchTimeoutMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await FlushAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            await FlushAsync(CancellationToken.None);
            _logger.LogInformation("CoinEventLoggerWorker stopped.");
        }
    }

    private void OnMessage(string json)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<TimelineEvent>(json);
            if (evt is null) return;

            var category = CategoryMap.GetValueOrDefault(evt.EventType, "UNKNOWN");
            var retentionDays = RetentionDays.GetValueOrDefault(category, _settings.DefaultRetentionDays);

            var doc = new CoinEventDocument
            {
                Symbol = evt.Symbol,
                EventType = evt.EventType,
                EventCategory = category,
                Timestamp = evt.Timestamp,
                UnixTimestamp = new DateTimeOffset(evt.Timestamp).ToUnixTimeMilliseconds(),
                SourceService = evt.SourceService,
                Severity = evt.Severity,
                CorrelationId = evt.CorrelationId,
                SessionId = evt.SessionId,
                Payload = ToBsonDocument(evt.Payload),
                Metadata = ToBsonDocument(evt.Metadata),
                Tags = evt.Tags,
                ExpiresAt = evt.Timestamp.AddDays(retentionDays),
            };

            _lock.Wait();
            try { _buffer.Add(doc); }
            finally { _lock.Release(); }

            // Flush if batch size reached
            if (_buffer.Count >= _settings.BatchSize)
                _ = Task.Run(() => FlushAsync(CancellationToken.None));

            Interlocked.Increment(ref _processedCount);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _logger.LogWarning(ex, "Failed to parse timeline event message");
        }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        List<CoinEventDocument> batch;

        await _lock.WaitAsync(ct);
        try
        {
            if (_buffer.Count == 0) return;
            batch = new List<CoinEventDocument>(_buffer);
            _buffer.Clear();
        }
        finally { _lock.Release(); }

        try
        {
            await _repository.InsertManyAsync(batch, ct);
            _lastFlush = DateTime.UtcNow;
            _logger.LogDebug("Flushed {Count} timeline events to MongoDB", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush {Count} timeline events to MongoDB", batch.Count);
        }
    }

    private static BsonDocument ToBsonDocument(Dictionary<string, object?> dict)
    {
        var doc = new BsonDocument();
        foreach (var (key, value) in dict)
            doc[key] = ToBsonValue(value);
        return doc;
    }

    private static BsonValue ToBsonValue(object? value) => value switch
    {
        null => BsonNull.Value,
        bool b => new BsonBoolean(b),
        int i => new BsonInt32(i),
        long l => new BsonInt64(l),
        double d => new BsonDouble(d),
        decimal dec => new BsonDouble((double)dec),
        string s => new BsonString(s),
        DateTime dt => new BsonDateTime(dt),
        System.Text.Json.JsonElement je => JsonElementToBsonValue(je),
        _ => BsonValue.Create(value.ToString())
    };

    private static BsonValue JsonElementToBsonValue(System.Text.Json.JsonElement je) => je.ValueKind switch
    {
        System.Text.Json.JsonValueKind.True => BsonBoolean.True,
        System.Text.Json.JsonValueKind.False => BsonBoolean.False,
        System.Text.Json.JsonValueKind.Null => BsonNull.Value,
        System.Text.Json.JsonValueKind.String => new BsonString(je.GetString() ?? string.Empty),
        System.Text.Json.JsonValueKind.Number when je.TryGetInt32(out var i) => new BsonInt32(i),
        System.Text.Json.JsonValueKind.Number when je.TryGetInt64(out var l) => new BsonInt64(l),
        System.Text.Json.JsonValueKind.Number => new BsonDouble(je.GetDouble()),
        _ => new BsonString(je.ToString())
    };
}
