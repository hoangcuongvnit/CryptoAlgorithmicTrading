using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using CryptoTrading.Shared.Session;
using Executor.API.Infrastructure;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Executor.API.Services;

/// <summary>
/// Background service that monitors session boundaries and automatically
/// closes all open positions before session end.
/// </summary>
public sealed class LiquidationOrchestrator : BackgroundService
{
    private readonly SessionClock _sessionClock;
    private readonly SessionTradingPolicy _policy;
    private readonly PositionTracker _positionTracker;
    private readonly OrderExecutionService _executionService;
    private readonly IConnectionMultiplexer _redis;
    private readonly SessionSettings _sessionSettings;
    private readonly ILogger<LiquidationOrchestrator> _logger;

    private string? _lastSessionId;
    private SessionPhase _lastPhase = SessionPhase.SessionClosed;
    private readonly HashSet<string> _closingSymbols = new(StringComparer.OrdinalIgnoreCase);

    public LiquidationOrchestrator(
        SessionClock sessionClock,
        SessionTradingPolicy policy,
        PositionTracker positionTracker,
        OrderExecutionService executionService,
        IConnectionMultiplexer redis,
        IOptions<SessionSettings> sessionSettings,
        ILogger<LiquidationOrchestrator> logger)
    {
        _sessionClock = sessionClock;
        _policy = policy;
        _positionTracker = positionTracker;
        _executionService = executionService;
        _redis = redis;
        _sessionSettings = sessionSettings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_sessionSettings.Enabled)
        {
            _logger.LogInformation("Session enforcement disabled. LiquidationOrchestrator will not run.");
            return;
        }

        _logger.LogInformation("LiquidationOrchestrator started. Session hours={Hours}, Liquidation window={Window}m",
            _sessionSettings.SessionHours, _sessionSettings.LiquidationWindowMinutes);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var session = _sessionClock.GetCurrentSession();
                await HandleSessionAsync(session, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "LiquidationOrchestrator cycle error");
            }
        }
    }

    private async Task HandleSessionAsync(SessionInfo session, CancellationToken ct)
    {
        // Detect session transition
        if (_lastSessionId is not null && _lastSessionId != session.SessionId)
        {
            await OnSessionEndAsync(_lastSessionId, ct);
            _closingSymbols.Clear();
        }

        if (_lastSessionId != session.SessionId)
        {
            _lastSessionId = session.SessionId;
            _logger.LogInformation("Session started: {SessionId}", session.SessionId);
            await PublishSessionEventAsync(SystemEventType.SessionStarted, session);
        }

        // Detect phase transition
        if (session.CurrentPhase != _lastPhase)
        {
            _logger.LogInformation("Session {SessionId} phase: {OldPhase} -> {NewPhase}",
                session.SessionId, _lastPhase, session.CurrentPhase);
            await OnPhaseTransitionAsync(session);
            _lastPhase = session.CurrentPhase;
        }

        // Act based on current phase
        switch (session.CurrentPhase)
        {
            case SessionPhase.SoftUnwind:
                LogOpenPositionsWarning(session);
                break;

            case SessionPhase.LiquidationOnly:
                await DispatchCloseOrdersAsync(session, OrderType.Limit, ct);
                break;

            case SessionPhase.ForcedFlatten:
                await DispatchCloseOrdersAsync(session, OrderType.Market, ct);
                break;
        }
    }

    private async Task OnPhaseTransitionAsync(SessionInfo session)
    {
        switch (session.CurrentPhase)
        {
            case SessionPhase.SoftUnwind:
                await PublishSessionEventAsync(SystemEventType.SessionEnding, session);
                break;
            case SessionPhase.LiquidationOnly:
                await PublishSessionEventAsync(SystemEventType.LiquidationStarted, session);
                break;
            case SessionPhase.ForcedFlatten:
                await PublishSessionEventAsync(SystemEventType.ForcedFlatten, session);
                break;
        }
    }

    private async Task OnSessionEndAsync(string sessionId, CancellationToken ct)
    {
        var openPositions = _positionTracker.GetOpenPositions();

        if (openPositions.Count == 0)
        {
            _logger.LogInformation("Session {SessionId} ended FLAT. All positions closed.", sessionId);
            var session = _sessionClock.GetCurrentSession();
            await PublishSessionEventAsync(SystemEventType.SessionFlat, session);
        }
        else
        {
            _logger.LogCritical(
                "Session {SessionId} ended NOT FLAT. {Count} positions remain open! Executing emergency close.",
                sessionId, openPositions.Count);

            var session = _sessionClock.GetCurrentSession();
            await PublishSessionEventAsync(SystemEventType.SessionNotFlat, session);

            // Emergency market close for all remaining positions
            await EmergencyFlattenAsync(ct);
        }
    }

    private async Task DispatchCloseOrdersAsync(SessionInfo session, OrderType orderType, CancellationToken ct)
    {
        var positions = _positionTracker.GetOpenPositions();

        if (positions.Count == 0)
            return;

        _logger.LogInformation(
            "Session {SessionId} [{Phase}]: {Count} open positions, {Minutes:F1}m to end. Dispatching close orders.",
            session.SessionId, session.CurrentPhase, positions.Count, session.TimeToEnd.TotalMinutes);

        foreach (var pos in positions)
        {
            // Extract symbol from anonymous object
            var symbol = pos.GetType().GetProperty("symbol")?.GetValue(pos)?.ToString();
            var quantity = (decimal)(pos.GetType().GetProperty("quantity")?.GetValue(pos) ?? 0m);
            var currentPrice = (decimal)(pos.GetType().GetProperty("currentPrice")?.GetValue(pos) ?? 0m);

            if (string.IsNullOrEmpty(symbol) || quantity <= 0)
                continue;

            // Avoid sending duplicate close orders for the same symbol within the same cycle
            if (!_closingSymbols.Add(symbol))
                continue;

            var closeOrder = new OrderRequest
            {
                Symbol = symbol,
                Side = OrderSide.Sell,
                Type = orderType,
                Quantity = quantity,
                Price = currentPrice,
                SessionId = session.SessionId,
                SessionPhase = session.CurrentPhase,
                IsReduceOnly = true,
                StrategyName = "LiquidationOrchestrator"
            };

            try
            {
                var result = await _executionService.ExecuteOrderAsync(closeOrder, ct);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "Liquidation close filled: {Symbol} qty={Qty} price={Price}",
                        symbol, result.FilledQty, result.FilledPrice);
                    _closingSymbols.Remove(symbol);
                }
                else
                {
                    _logger.LogWarning(
                        "Liquidation close failed: {Symbol}: {Error}. Will retry.",
                        symbol, result.ErrorMessage);
                    _closingSymbols.Remove(symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching liquidation close for {Symbol}", symbol);
                _closingSymbols.Remove(symbol);
            }
        }
    }

    private async Task EmergencyFlattenAsync(CancellationToken ct)
    {
        var positions = _positionTracker.GetOpenPositions();
        foreach (var pos in positions)
        {
            var symbol = pos.GetType().GetProperty("symbol")?.GetValue(pos)?.ToString();
            var quantity = (decimal)(pos.GetType().GetProperty("quantity")?.GetValue(pos) ?? 0m);

            if (string.IsNullOrEmpty(symbol) || quantity <= 0)
                continue;

            var emergencyOrder = new OrderRequest
            {
                Symbol = symbol,
                Side = OrderSide.Sell,
                Type = OrderType.Market,
                Quantity = quantity,
                Price = 0,
                IsReduceOnly = true,
                StrategyName = "LiquidationOrchestrator"
            };

            try
            {
                await _executionService.ExecuteOrderAsync(emergencyOrder, ct);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "EMERGENCY FLATTEN FAILED for {Symbol}!", symbol);
            }
        }
    }

    private void LogOpenPositionsWarning(SessionInfo session)
    {
        var positions = _positionTracker.GetOpenPositions();
        if (positions.Count > 0)
        {
            _logger.LogWarning(
                "Session {SessionId} entering soft unwind with {Count} open positions. " +
                "Liquidation window in {Minutes:F1}m",
                session.SessionId, positions.Count, session.TimeToLiquidation.TotalMinutes);
        }
    }

    private async Task PublishSessionEventAsync(SystemEventType eventType, SessionInfo session)
    {
        try
        {
            var db = _redis.GetDatabase();
            var eventData = new SystemEvent
            {
                Type = eventType,
                ServiceName = "LiquidationOrchestrator",
                Message = $"Session {session.SessionId} - {eventType} (phase={session.CurrentPhase}, minutesToEnd={session.TimeToEnd.TotalMinutes:F1})",
                Timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(eventData, TradingJsonContext.Default.SystemEvent);
            await db.PublishAsync(new RedisChannel(RedisChannels.SessionEvents, RedisChannel.PatternMode.Literal), json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish session event {EventType}", eventType);
        }
    }
}
