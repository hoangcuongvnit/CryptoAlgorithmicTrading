using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Session;
using CryptoTrading.Shared.Timeline;
using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using System.Diagnostics;

namespace Executor.API.Services;

/// <summary>
/// Core order execution logic shared by gRPC service and LiquidationOrchestrator.
/// </summary>
public sealed class OrderExecutionService
{
    private readonly TradingSettings _tradingSettings;
    private readonly OrderAmountLimitValidator _orderAmountValidator;
    private readonly BuyBudgetGuardService _buyBudgetGuard;
    private readonly BinanceOrderClient _binanceOrderClient;
    private readonly SpreadFilterService _spreadFilter;
    private readonly PriceConsensusService _consensus;
    private readonly OrderWriteQueue _orderWriteQueue;
    private readonly AuditStreamPublisher _auditStreamPublisher;
    private readonly LedgerEventPublisher _ledgerEventPublisher;
    private readonly SystemEventPublisher _systemEvents;
    private readonly PositionTracker _positionTracker;
    private readonly OrderExecutionMetrics _metrics;
    private readonly SessionClock _sessionClock;
    private readonly SessionSettings _sessionSettings;
    private readonly ITimelineEventPublisher _timeline;
    private readonly ILogger<OrderExecutionService> _logger;

    public OrderExecutionService(
        IOptions<TradingSettings> tradingSettings,
        OrderAmountLimitValidator orderAmountValidator,
        BuyBudgetGuardService buyBudgetGuard,
        BinanceOrderClient binanceOrderClient,
        SpreadFilterService spreadFilter,
        PriceConsensusService consensus,
        OrderWriteQueue orderWriteQueue,
        AuditStreamPublisher auditStreamPublisher,
        LedgerEventPublisher ledgerEventPublisher,
        SystemEventPublisher systemEvents,
        PositionTracker positionTracker,
        OrderExecutionMetrics metrics,
        SessionClock sessionClock,
        IOptions<SessionSettings> sessionSettings,
        ITimelineEventPublisher timeline,
        ILogger<OrderExecutionService> logger)
    {
        _tradingSettings = tradingSettings.Value;
        _orderAmountValidator = orderAmountValidator;
        _buyBudgetGuard = buyBudgetGuard;
        _binanceOrderClient = binanceOrderClient;
        _spreadFilter = spreadFilter;
        _consensus = consensus;
        _orderWriteQueue = orderWriteQueue;
        _auditStreamPublisher = auditStreamPublisher;
        _ledgerEventPublisher = ledgerEventPublisher;
        _systemEvents = systemEvents;
        _positionTracker = positionTracker;
        _metrics = metrics;
        _sessionClock = sessionClock;
        _sessionSettings = sessionSettings.Value;
        _timeline = timeline;
        _logger = logger;
    }

    public async Task<OrderResult> ExecuteOrderAsync(OrderRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        var session = _sessionSettings.Enabled ? _sessionClock.GetCurrentSession() : null;
        var orderRequest = request with
        {
            SessionId = request.SessionId ?? session?.SessionId
        };

        var amountValidation = await _orderAmountValidator.ValidateAsync(orderRequest, ct);
        if (!amountValidation.Passed)
        {
            return new OrderResult
            {
                OrderId = Guid.NewGuid().ToString("N"),
                Symbol = orderRequest.Symbol,
                Side = orderRequest.Side,
                Success = false,
                ErrorMessage = amountValidation.ErrorMessage ?? "Order amount validation failed.",
                ErrorCode = amountValidation.ErrorCode,
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = false,
                SessionId = orderRequest.SessionId
            };
        }

        if (orderRequest.Side == OrderSide.Sell &&
            !orderRequest.IsReduceOnly &&
            !_positionTracker.TryValidateSellQuantity(orderRequest.Symbol, orderRequest.Quantity, out var availableQuantity, out var sellGuardError))
        {
            if (availableQuantity <= 0m)
            {
                return new OrderResult
                {
                    OrderId = Guid.NewGuid().ToString("N"),
                    Symbol = orderRequest.Symbol,
                    Side = orderRequest.Side,
                    Success = false,
                    ErrorMessage = string.IsNullOrWhiteSpace(sellGuardError)
                        ? $"Sell blocked: no local position tracked for {orderRequest.Symbol}."
                        : sellGuardError,
                    ErrorCode = TradingErrorCode.InsufficientPositionQuantity,
                    Timestamp = DateTime.UtcNow,
                    IsPaperTrade = false,
                    SessionId = orderRequest.SessionId
                };
            }

            _logger.LogWarning(
                "Sell quantity clamped for {Symbol}: requested {Requested} exceeds tracked position {Available}. Proceeding with available quantity.",
                orderRequest.Symbol, orderRequest.Quantity, availableQuantity);
            orderRequest = orderRequest with { Quantity = availableQuantity };
        }

        if (orderRequest.Side == OrderSide.Buy)
        {
            var buyBudget = await _buyBudgetGuard.ValidateAsync(orderRequest, amountValidation.EffectivePrice, ct);
            if (!buyBudget.Passed)
            {
                return new OrderResult
                {
                    OrderId = Guid.NewGuid().ToString("N"),
                    Symbol = orderRequest.Symbol,
                    Side = orderRequest.Side,
                    Success = false,
                    ErrorMessage = buyBudget.ErrorMessage,
                    ErrorCode = buyBudget.ErrorCode,
                    Timestamp = DateTime.UtcNow,
                    IsPaperTrade = false,
                    SessionId = orderRequest.SessionId
                };
            }
        }

        var (consensusPassed, _, consensusReason) = await _consensus.ValidateAsync(orderRequest.Symbol, ct);
        if (!consensusPassed)
        {
            return new OrderResult
            {
                OrderId = Guid.NewGuid().ToString("N"),
                Symbol = orderRequest.Symbol,
                Side = orderRequest.Side,
                Success = false,
                ErrorMessage = consensusReason ?? "Price consensus check failed",
                ErrorCode = TradingErrorCode.PriceConsensusFailure,
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = false,
                SessionId = orderRequest.SessionId
            };
        }

        var (spreadPassed, spreadReason) = await _spreadFilter.CheckSpreadAsync(orderRequest.Symbol, ct);
        if (!spreadPassed)
        {
            return new OrderResult
            {
                OrderId = Guid.NewGuid().ToString("N"),
                Symbol = orderRequest.Symbol,
                Side = orderRequest.Side,
                Success = false,
                ErrorMessage = spreadReason ?? "Spread limit exceeded",
                ErrorCode = TradingErrorCode.SpreadLimitExceeded,
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = false,
                SessionId = orderRequest.SessionId
            };
        }

        _metrics.RecordOrderPlaced(orderRequest.Symbol, orderRequest.Side.ToString(), orderRequest.Quantity);

        OrderResult orderResult;
        try
        {
            orderResult = await _binanceOrderClient.PlaceOrderAsync(orderRequest, ct);
            orderResult = orderResult with
            {
                SessionId = orderRequest.SessionId,
                ForcedLiquidation = orderRequest.IsReduceOnly && orderRequest.StrategyName == "LiquidationOrchestrator",
                LiquidationReason = orderRequest.IsReduceOnly && orderRequest.StrategyName == "LiquidationOrchestrator"
                    ? LiquidationReason.Deadline
                    : LiquidationReason.None
            };
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit breaker open for {Symbol} - exchange requests blocked", orderRequest.Symbol);
            orderResult = new OrderResult
            {
                OrderId = Guid.NewGuid().ToString("N"),
                Symbol = orderRequest.Symbol,
                Success = false,
                ErrorMessage = "Exchange circuit breaker is open. Too many recent failures.",
                ErrorCode = TradingErrorCode.ExchangeCircuitOpen,
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = false,
                SessionId = orderRequest.SessionId
            };
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Order execution timed out for {Symbol}", orderRequest.Symbol);
            orderResult = new OrderResult
            {
                OrderId = Guid.NewGuid().ToString("N"),
                Symbol = orderRequest.Symbol,
                Success = false,
                ErrorMessage = "Order execution timed out.",
                ErrorCode = TradingErrorCode.ExchangeTimeout,
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = false,
                SessionId = orderRequest.SessionId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order execution failed for {Symbol}", orderRequest.Symbol);
            orderResult = new OrderResult
            {
                OrderId = Guid.NewGuid().ToString("N"),
                Symbol = orderRequest.Symbol,
                Success = false,
                ErrorMessage = ex.Message,
                ErrorCode = TradingErrorCode.ExchangeRequestFailed,
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = false,
                SessionId = orderRequest.SessionId
            };
        }

        stopwatch.Stop();
        _metrics.RecordOrderLatency(stopwatch.Elapsed.TotalMilliseconds, orderRequest.Symbol);

        PositionTracker.OpenPosition? positionBeforeFill = null;
        if (orderResult.Success && orderRequest.Side == OrderSide.Sell)
        {
            positionBeforeFill = _positionTracker
                .GetRawPositions()
                .FirstOrDefault(p => string.Equals(p.Symbol, orderRequest.Symbol, StringComparison.OrdinalIgnoreCase));
        }

        if (orderResult.Success)
        {
            _metrics.RecordOrderFilled(orderRequest.Symbol, orderResult.FilledQty, orderResult.FilledPrice);
            _positionTracker.OnOrderFilled(orderRequest, orderResult);

            _ = _ledgerEventPublisher
                .PublishFromSuccessfulExecutionAsync(orderRequest, orderResult, positionBeforeFill, CancellationToken.None)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Ledger event publish failed for {Symbol}", orderRequest.Symbol);
                    }
                }, TaskScheduler.Default);

            if (orderRequest.Price > 0 && orderResult.FilledPrice > 0)
            {
                var slippage = Math.Abs(orderResult.FilledPrice - orderRequest.Price) / orderRequest.Price;
                if (slippage > _tradingSettings.SpreadFilter.SlippageTolerance)
                {
                    _logger.LogWarning(
                        "Slippage {Slippage:P3} exceeds tolerance {Tolerance:P3} - {Symbol} {Side} order {OrderId} (requested={Requested}, filled={Filled})",
                        slippage,
                        _tradingSettings.SpreadFilter.SlippageTolerance,
                        orderRequest.Symbol,
                        orderRequest.Side,
                        orderResult.OrderId,
                        orderRequest.Price,
                        orderResult.FilledPrice);
                }
            }
        }

        _orderWriteQueue.TryEnqueue(orderRequest, orderResult);

        _ = _auditStreamPublisher.PublishAsync(orderRequest, orderResult)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.LogError(t.Exception, "Audit stream publish failed for {Symbol}", orderRequest.Symbol);
                    _ = _systemEvents.PublishAsync(new SystemEvent
                    {
                        Type = SystemEventType.Error,
                        ServiceName = "Executor",
                        Message = $"Audit stream publish failed for {orderRequest.Symbol}: {t.Exception?.InnerException?.Message}",
                        ErrorCode = TradingErrorCode.AuditStreamFailed,
                        Symbol = orderRequest.Symbol,
                        Timestamp = DateTime.UtcNow
                    }, CancellationToken.None);
                }
                else
                {
                    _metrics.RecordAuditEventPublished();
                }
            }, TaskScheduler.Default);

        if (orderResult.Success)
        {
            _ = _timeline.PublishAsync(new TimelineEvent
            {
                SourceService = "Executor",
                EventType = TimelineEventTypes.OrderFilled,
                Symbol = orderResult.Symbol,
                SessionId = orderResult.SessionId,
                Severity = TimelineSeverity.Info,
                Payload = new Dictionary<string, object?>
                {
                    ["order_id"] = orderResult.OrderId,
                    ["side"] = orderRequest.Side.ToString(),
                    ["filled_qty"] = orderResult.FilledQty,
                    ["filled_price"] = orderResult.FilledPrice,
                    ["trading_mode"] = "live",
                },
                Tags = [orderRequest.Side.ToString().ToLowerInvariant(), "filled"],
            }, ct);
        }

        return orderResult;
    }
}
