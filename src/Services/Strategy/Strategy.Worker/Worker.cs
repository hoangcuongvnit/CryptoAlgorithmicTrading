using CryptoTrading.Executor.Grpc;
using CryptoTrading.RiskGuard.Grpc;
using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using CryptoTrading.Shared.Session;
using CryptoTrading.Shared.Timeline;
using Grpc.Core;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Strategy.Worker.Infrastructure;
using Strategy.Worker.Services;
using System.Text.Json;

namespace Strategy.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly RiskGuardService.RiskGuardServiceClient _riskGuardClient;
    private readonly OrderExecutorService.OrderExecutorServiceClient _executorClient;
    private readonly SignalToOrderMapper _mapper;
    private readonly ITimelineEventPublisher _timeline;
    private readonly StrategySystemEventPublisher _systemEvents;
    private readonly SessionClock _sessionClock;
    private readonly SessionTradingPolicy _sessionPolicy;
    private readonly SessionSettings _sessionSettings;
    private ISubscriber? _subscriber;

    public Worker(
        ILogger<Worker> logger,
        IConnectionMultiplexer redis,
        RiskGuardService.RiskGuardServiceClient riskGuardClient,
        OrderExecutorService.OrderExecutorServiceClient executorClient,
        SignalToOrderMapper mapper,
        ITimelineEventPublisher timeline,
        StrategySystemEventPublisher systemEvents,
        SessionClock sessionClock,
        SessionTradingPolicy sessionPolicy,
        IOptions<SessionSettings> sessionSettings)
    {
        _logger = logger;
        _redis = redis;
        _riskGuardClient = riskGuardClient;
        _executorClient = executorClient;
        _mapper = mapper;
        _timeline = timeline;
        _systemEvents = systemEvents;
        _sessionClock = sessionClock;
        _sessionPolicy = sessionPolicy;
        _sessionSettings = sessionSettings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _subscriber = _redis.GetSubscriber();

        await _subscriber.SubscribeAsync(
            new RedisChannel(RedisChannels.SignalPattern, RedisChannel.PatternMode.Pattern),
            async (_, message) => await OnSignalAsync(message, stoppingToken));

        _logger.LogInformation("Strategy worker subscribed to {Pattern}", RedisChannels.SignalPattern);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Strategy worker stopping...");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null)
        {
            await _subscriber.UnsubscribeAsync(new RedisChannel(RedisChannels.SignalPattern, RedisChannel.PatternMode.Pattern));
        }

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Returns true (and logs) when the platform trading mode is not TradingEnabled.
    /// Fails open on Redis errors so a Redis outage never silently kills order flow.
    /// </summary>
    private async Task<bool> IsTradingBlockedAsync(string symbol, CancellationToken ct)
    {
        const string TradingModeKey = "executor:trading:mode";
        try
        {
            var db = _redis.GetDatabase();
            var mode = (string?)await db.StringGetAsync(TradingModeKey).WaitAsync(ct);

            if (string.IsNullOrEmpty(mode) || mode == "TradingEnabled")
                return false;

            _logger.LogInformation(
                "Signal for {Symbol} skipped: trading mode is {Mode}. Entry orders blocked until trading is resumed.",
                symbol, mode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "IsTradingBlockedAsync: Redis unavailable — failing open to avoid blocking order flow");
            return false;
        }
    }

    private async Task OnSignalAsync(RedisValue payload, CancellationToken cancellationToken)
    {
        TradeSignal? signal;

        try
        {
            var jsonString = (string?)payload;
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                _logger.LogWarning("Received empty payload");
                return;
            }

            signal = JsonSerializer.Deserialize(jsonString, TradingJsonContext.Default.TradeSignal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize trade signal payload. Raw String={PayloadString} | RedisValue={RedisValue}",
                (string?)payload, payload.ToString());
            return;
        }

        if (signal is null)
        {
            return;
        }

        // Session-phase gate: skip new entry signals outside Open/SoftUnwind
        SessionInfo? session = null;
        if (_sessionSettings.Enabled)
        {
            session = _sessionClock.GetCurrentSession();

            if (!_sessionPolicy.CanOpenNewPosition(session)
                && session.CurrentPhase != SessionPhase.SoftUnwind)
            {
                _logger.LogDebug(
                    "Signal for {Symbol} skipped: session {SessionId} is in {Phase} phase",
                    signal.Symbol, session.SessionId, session.CurrentPhase);
                return;
            }
        }

        // Trading-mode gate: skip entry orders while system is not in TradingEnabled state.
        // Executor would reject them anyway; checking here prevents noisy FAILED order records.
        var tradingModeBlocked = await IsTradingBlockedAsync(signal.Symbol, cancellationToken);
        if (tradingModeBlocked)
            return;

        if (!_mapper.TryMap(signal, out var order, session) || order is null)
        {
            _logger.LogDebug("Signal for {Symbol} ignored by strategy mapper", signal.Symbol);
            return;
        }

        // Fire-and-forget: timeline publish must never delay order flow
        _ = _timeline.PublishAsync(new TimelineEvent
        {
            SourceService = "Strategy",
            EventType = TimelineEventTypes.OrderMapped,
            Symbol = order.Symbol,
            SessionId = order.SessionId,
            Severity = TimelineSeverity.Info,
            Payload = new Dictionary<string, object?>
            {
                ["side"] = order.Side.ToString(),
                ["quantity"] = order.Quantity,
                ["price"] = order.Price,
                ["stop_loss"] = order.StopLoss,
                ["take_profit"] = order.TakeProfit,
                ["order_type"] = order.Type.ToString(),
            },
            Tags = [order.Side.ToString().ToLowerInvariant(), "order_mapped"],
        }, cancellationToken);

        // RiskGuard call with 5-second timeout guard
        ValidateOrderReply riskResponse;
        try
        {
            using var riskCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            riskCts.CancelAfter(TimeSpan.FromSeconds(5));
            riskResponse = await _riskGuardClient.ValidateOrderAsync(new ValidateOrderRequest
            {
                Symbol = order.Symbol,
                Side = order.Side.ToString(),
                Quantity = (double)order.Quantity,
                EntryPrice = (double)order.Price,
                StopLoss = (double)order.StopLoss,
                TakeProfit = (double)order.TakeProfit,
                SessionId = order.SessionId ?? string.Empty,
                SessionPhase = order.SessionPhase?.ToString() ?? string.Empty,
                IsReduceOnly = order.IsReduceOnly
            }, cancellationToken: riskCts.Token);
        }
        catch (RpcException rpcEx)
        {
            var errorCode = rpcEx.StatusCode == StatusCode.DeadlineExceeded
                ? TradingErrorCode.RiskGuardCallTimeout
                : TradingErrorCode.RiskGuardUnavailable;
            _logger.LogError(rpcEx, "RiskGuard gRPC call failed for {Symbol}: {Status}", order.Symbol, rpcEx.Status.Detail);
            _ = _systemEvents.PublishAsync(new SystemEvent
            {
                Type = SystemEventType.Error,
                ServiceName = "Strategy",
                Message = $"RiskGuard unavailable for {order.Symbol}: [{errorCode}] {rpcEx.Status.Detail}",
                ErrorCode = errorCode,
                Symbol = order.Symbol,
                Timestamp = DateTime.UtcNow
            }, CancellationToken.None);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling RiskGuard for {Symbol}", order.Symbol);
            _ = _systemEvents.PublishAsync(new SystemEvent
            {
                Type = SystemEventType.Error,
                ServiceName = "Strategy",
                Message = $"RiskGuard call failed for {order.Symbol}: {ex.Message}",
                ErrorCode = TradingErrorCode.RiskGuardUnavailable,
                Symbol = order.Symbol,
                Timestamp = DateTime.UtcNow
            }, CancellationToken.None);
            return;
        }

        if (!riskResponse.IsApproved)
        {
            _logger.LogInformation(
                "Order rejected by RiskGuard for {Symbol}: {Reason}",
                order.Symbol,
                riskResponse.RejectionReason);
            return;
        }

        var finalQuantity = riskResponse.AdjustedQuantity > 0
            ? (decimal)riskResponse.AdjustedQuantity
            : order.Quantity;

        // Executor call with 5-second timeout guard
        PlaceOrderReply executionReply;
        try
        {
            using var execCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            execCts.CancelAfter(TimeSpan.FromSeconds(5));
            executionReply = await _executorClient.PlaceOrderAsync(new PlaceOrderRequest
            {
                Symbol = order.Symbol,
                Side = order.Side.ToString(),
                OrderType = order.Type.ToString(),
                Quantity = (double)finalQuantity,
                Price = (double)order.Price,
                StopLoss = (double)order.StopLoss,
                TakeProfit = (double)order.TakeProfit,
                Strategy = order.StrategyName,
                SessionId = order.SessionId ?? string.Empty,
                SessionPhase = order.SessionPhase?.ToString() ?? string.Empty,
                IsReduceOnly = order.IsReduceOnly
            }, cancellationToken: execCts.Token);
        }
        catch (RpcException rpcEx)
        {
            var errorCode = rpcEx.StatusCode == StatusCode.DeadlineExceeded
                ? TradingErrorCode.ExecutorCallTimeout
                : TradingErrorCode.ExecutorUnavailable;
            _logger.LogError(rpcEx, "Executor gRPC call failed for {Symbol}: {Status}", order.Symbol, rpcEx.Status.Detail);
            _ = _systemEvents.PublishAsync(new SystemEvent
            {
                Type = SystemEventType.Error,
                ServiceName = "Strategy",
                Message = $"Executor unavailable for {order.Symbol}: [{errorCode}] {rpcEx.Status.Detail}",
                ErrorCode = errorCode,
                Symbol = order.Symbol,
                Timestamp = DateTime.UtcNow
            }, CancellationToken.None);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Executor for {Symbol}", order.Symbol);
            _ = _systemEvents.PublishAsync(new SystemEvent
            {
                Type = SystemEventType.Error,
                ServiceName = "Strategy",
                Message = $"Executor call failed for {order.Symbol}: {ex.Message}",
                ErrorCode = TradingErrorCode.ExecutorUnavailable,
                Symbol = order.Symbol,
                Timestamp = DateTime.UtcNow
            }, CancellationToken.None);
            return;
        }

        if (executionReply.Success)
        {
            _logger.LogInformation(
                "Order executed for {Symbol} | Qty={Qty} | Price={Price} | Paper={Paper} | Session={Session}",
                order.Symbol,
                executionReply.FilledQty,
                executionReply.FilledPrice,
                executionReply.IsPaper,
                order.SessionId);
            return;
        }

        _logger.LogWarning(
            "Order execution failed for {Symbol}: {Error}",
            order.Symbol,
            executionReply.ErrorMessage);
    }
}
