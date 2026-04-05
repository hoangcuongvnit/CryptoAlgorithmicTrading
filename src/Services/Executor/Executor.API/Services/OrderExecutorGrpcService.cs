using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Session;
using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Executor.API.Protos;
using Grpc.Core;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Executor.API.Services;

public sealed class OrderExecutorGrpcService : OrderExecutorService.OrderExecutorServiceBase
{
    private readonly TradingSettings _tradingSettings;
    private readonly OrderAmountLimitValidator _orderAmountValidator;
    private readonly BinanceOrderClient _binanceOrderClient;
    private readonly OrderWriteQueue _orderWriteQueue;
    private readonly AuditStreamPublisher _auditStreamPublisher;
    private readonly SystemEventPublisher _systemEvents;
    private readonly PositionTracker _positionTracker;
    private readonly OrderExecutionMetrics _metrics;
    private readonly SessionClock _sessionClock;
    private readonly SessionTradingPolicy _sessionPolicy;
    private readonly SessionSettings _sessionSettings;
    private readonly RecoveryStateService _recoveryState;
    private readonly ShutdownOperationService _shutdownOp;
    private readonly ILogger<OrderExecutorGrpcService> _logger;

    public OrderExecutorGrpcService(
        IOptions<TradingSettings> tradingSettings,
        OrderAmountLimitValidator orderAmountValidator,
        BinanceOrderClient binanceOrderClient,
        OrderWriteQueue orderWriteQueue,
        AuditStreamPublisher auditStreamPublisher,
        SystemEventPublisher systemEvents,
        PositionTracker positionTracker,
        OrderExecutionMetrics metrics,
        SessionClock sessionClock,
        SessionTradingPolicy sessionPolicy,
        IOptions<SessionSettings> sessionSettings,
        RecoveryStateService recoveryState,
        ShutdownOperationService shutdownOp,
        ILogger<OrderExecutorGrpcService> logger)
    {
        _tradingSettings = tradingSettings.Value;
        _orderAmountValidator = orderAmountValidator;
        _binanceOrderClient = binanceOrderClient;
        _orderWriteQueue = orderWriteQueue;
        _auditStreamPublisher = auditStreamPublisher;
        _systemEvents = systemEvents;
        _positionTracker = positionTracker;
        _metrics = metrics;
        _sessionClock = sessionClock;
        _sessionPolicy = sessionPolicy;
        _sessionSettings = sessionSettings.Value;
        _recoveryState = recoveryState;
        _shutdownOp = shutdownOp;
        _logger = logger;
    }

    public override async Task<PlaceOrderReply> PlaceOrder(PlaceOrderRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var orderRequestResult = TryBuildOrderRequest(request, out var orderRequest, out var validationError);
        OrderResult orderResult;

        if (!orderRequestResult || orderRequest is null)
        {
            _metrics.RecordOrderRejected(request.Symbol, "Invalid order request");
            orderResult = BuildFailureResult(request.Symbol, validationError ?? "Invalid order request.", TradingErrorCode.InvalidOrderParameters);
            return await PersistAndReplyAsync(request, null, orderResult, context.CancellationToken, stopwatch);
        }

        if (_tradingSettings.GlobalKillSwitch)
        {
            _metrics.RecordOrderRejected(orderRequest.Symbol, "Global kill switch");
            orderResult = BuildFailureResult(orderRequest.Symbol, "Global kill switch is enabled.", TradingErrorCode.GlobalKillSwitchActive);
            return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
        }

        // Shutdown/exit-only gate — block new-position orders during close-all operation
        if (_shutdownOp.IsExitOnlyMode && !request.IsReduceOnly)
        {
            _metrics.RecordOrderRejected(orderRequest.Symbol, "Shutdown exit-only mode");
            orderResult = BuildFailureResult(
                orderRequest.Symbol,
                "System is in exit-only mode. New position orders are blocked during the close-all operation.",
                TradingErrorCode.ShutdownExitOnlyMode);
            return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
        }

        // Recovery gate — block new-position orders until reconciliation is complete
        if (_recoveryState.IsBlockingNewOrders && !request.IsReduceOnly)
        {
            _metrics.RecordOrderRejected(orderRequest.Symbol, "Recovery mode");
            orderResult = BuildFailureResult(
                orderRequest.Symbol,
                $"System is in {_recoveryState.CurrentState} mode. New position orders blocked until recovery completes.",
                TradingErrorCode.RecoveryModeBlocked);
            return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
        }

        // Session boundary enforcement
        if (_sessionSettings.Enabled)
        {
            var session = _sessionClock.GetCurrentSession();

            // Reject cross-session orders
            if (!string.IsNullOrEmpty(request.SessionId) && request.SessionId != session.SessionId)
            {
                _metrics.RecordOrderRejected(orderRequest.Symbol, "Stale session");
                orderResult = BuildFailureResult(orderRequest.Symbol,
                    $"Stale session order. Current: {session.SessionId}, Order: {request.SessionId}",
                    TradingErrorCode.StaleSession);
                return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
            }

            // Block new positions in liquidation/forced-flatten phases
            if (!request.IsReduceOnly && !_sessionPolicy.CanOpenNewPosition(session))
            {
                _metrics.RecordOrderRejected(orderRequest.Symbol, "Session liquidation window");
                orderResult = BuildFailureResult(orderRequest.Symbol,
                    $"Session {session.SessionId} is in {session.CurrentPhase} phase. New positions blocked.",
                    TradingErrorCode.SessionPhaseBlocked);
                return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
            }
        }

        if (_tradingSettings.AllowedSymbols.Count > 0 &&
            !_tradingSettings.AllowedSymbols.Contains(orderRequest.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            _metrics.RecordOrderRejected(orderRequest.Symbol, "Symbol not allowed");
            orderResult = BuildFailureResult(orderRequest.Symbol, $"Symbol {orderRequest.Symbol} is not allowed.", TradingErrorCode.SymbolNotAllowed);
            return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
        }

        var amountValidation = await _orderAmountValidator.ValidateAsync(orderRequest, context.CancellationToken);
        if (!amountValidation.Passed)
        {
            _metrics.RecordOrderRejected(orderRequest.Symbol, amountValidation.ErrorMessage ?? "Order amount validation failed");
            orderResult = BuildFailureResult(
                orderRequest.Symbol,
                amountValidation.ErrorMessage ?? "Order amount validation failed.",
                amountValidation.ErrorCode);
            return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
        }

        _metrics.RecordOrderPlaced(orderRequest.Symbol, orderRequest.Side.ToString(), orderRequest.Quantity);

        try
        {
            orderResult = await _binanceOrderClient.PlaceOrderAsync(orderRequest, context.CancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Order execution timed out for {Symbol}", orderRequest.Symbol);
            orderResult = BuildFailureResult(orderRequest.Symbol, "Order execution timed out.", TradingErrorCode.ExchangeTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order execution failed unexpectedly for {Symbol}", orderRequest.Symbol);
            orderResult = BuildFailureResult(orderRequest.Symbol, ex.Message, TradingErrorCode.ExchangeRequestFailed);
        }

        if (orderResult.Success)
        {
            _metrics.RecordOrderFilled(orderRequest.Symbol, orderResult.FilledQty, orderResult.FilledPrice);
        }

        return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
    }

    private async Task<PlaceOrderReply> PersistAndReplyAsync(
        PlaceOrderRequest originalRequest,
        OrderRequest? mappedRequest,
        OrderResult result,
        CancellationToken cancellationToken,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        var latencyMs = stopwatch.Elapsed.TotalMilliseconds;

        var requestForPersistence = mappedRequest ?? BuildFallbackOrderRequest(originalRequest);

        // Record latency metric
        _metrics.RecordOrderLatency(latencyMs, requestForPersistence.Symbol);

        // Enqueue DB write — never blocks gRPC response
        _orderWriteQueue.TryEnqueue(requestForPersistence, result);

        if (result.Success)
        {
            _positionTracker.OnOrderFilled(requestForPersistence, result);
        }
        else
        {
            // Notify Telegram immediately for all order failures
            _ = _systemEvents.PublishAsync(new SystemEvent
            {
                Type = SystemEventType.OrderRejected,
                ServiceName = "Executor",
                Message = $"Order failed for {requestForPersistence.Symbol}: [{result.ErrorCode}] {result.ErrorMessage}",
                ErrorCode = result.ErrorCode,
                Symbol = requestForPersistence.Symbol,
                Timestamp = DateTime.UtcNow
            }, CancellationToken.None);
        }

        // Audit stream is non-critical — fire-and-forget
        _ = _auditStreamPublisher.PublishAsync(requestForPersistence, result)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.LogError(t.Exception, "Audit stream publish failed for {Symbol}", requestForPersistence.Symbol);
                    _ = _systemEvents.PublishAsync(new SystemEvent
                    {
                        Type = SystemEventType.Error,
                        ServiceName = "Executor",
                        Message = $"Audit stream publish failed for {requestForPersistence.Symbol}: {t.Exception?.InnerException?.Message}",
                        ErrorCode = TradingErrorCode.AuditStreamFailed,
                        Symbol = requestForPersistence.Symbol,
                        Timestamp = DateTime.UtcNow
                    }, CancellationToken.None);
                }
                else
                {
                    _metrics.RecordAuditEventPublished();
                }
            }, TaskScheduler.Default);

        return new PlaceOrderReply
        {
            Success = result.Success,
            OrderId = result.OrderId,
            FilledPrice = (double)result.FilledPrice,
            FilledQty = (double)result.FilledQty,
            ErrorMessage = result.ErrorMessage,
            IsPaper = result.IsPaperTrade,
            SessionId = result.SessionId ?? string.Empty,
            ForcedLiquidation = result.ForcedLiquidation,
            LiquidationReason = result.LiquidationReason.ToString()
        };
    }

    private static OrderResult BuildFailureResult(
        string symbol,
        string errorMessage,
        TradingErrorCode errorCode = TradingErrorCode.UnknownError)
    {
        return new OrderResult
        {
            OrderId = Guid.NewGuid().ToString("N"),
            Symbol = string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.ToUpperInvariant(),
            Success = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            Timestamp = DateTime.UtcNow,
            IsPaperTrade = false
        };
    }

    private static OrderRequest BuildFallbackOrderRequest(PlaceOrderRequest request)
    {
        _ = Enum.TryParse<OrderSide>(request.Side, true, out var side);
        _ = Enum.TryParse<OrderType>(request.OrderType, true, out var orderType);

        return new OrderRequest
        {
            Symbol = string.IsNullOrWhiteSpace(request.Symbol) ? "UNKNOWN" : request.Symbol.ToUpperInvariant(),
            Side = side,
            Type = orderType,
            Quantity = request.Quantity > 0 ? (decimal)request.Quantity : 0m,
            Price = request.Price > 0 ? (decimal)request.Price : 0m,
            StopLoss = request.StopLoss > 0 ? (decimal)request.StopLoss : 0m,
            TakeProfit = request.TakeProfit > 0 ? (decimal)request.TakeProfit : 0m,
            StrategyName = request.Strategy
        };
    }

    private static bool TryBuildOrderRequest(PlaceOrderRequest request, out OrderRequest? orderRequest, out string? validationError)
    {
        orderRequest = null;

        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            validationError = "Symbol is required.";
            return false;
        }

        if (!Enum.TryParse<OrderSide>(request.Side, true, out var side))
        {
            validationError = "Invalid side. Supported values: Buy, Sell.";
            return false;
        }

        if (!Enum.TryParse<OrderType>(request.OrderType, true, out var orderType))
        {
            validationError = "Invalid order type. Supported values: Market, Limit, StopLimit.";
            return false;
        }

        if (request.Quantity <= 0)
        {
            validationError = "Quantity must be greater than zero.";
            return false;
        }

        if (request.Price < 0)
        {
            validationError = "Price cannot be negative.";
            return false;
        }

        var price = request.Price > 0 ? (decimal)request.Price : 0m;
        var stopLoss = request.StopLoss > 0 ? (decimal)request.StopLoss : 0m;
        var takeProfit = request.TakeProfit > 0 ? (decimal)request.TakeProfit : 0m;

        if (stopLoss > 0 && takeProfit > 0 && price > 0)
        {
            if (side == OrderSide.Buy && !(stopLoss < price && price < takeProfit))
            {
                validationError = "For Buy orders, stop loss must be lower than entry and take profit must be higher than entry.";
                return false;
            }

            if (side == OrderSide.Sell && !(takeProfit < price && price < stopLoss))
            {
                validationError = "For Sell orders, take profit must be lower than entry and stop loss must be higher than entry.";
                return false;
            }
        }

        orderRequest = new OrderRequest
        {
            Symbol = request.Symbol.Trim().ToUpperInvariant(),
            Side = side,
            Type = orderType,
            Quantity = (decimal)request.Quantity,
            Price = price,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            StrategyName = request.Strategy?.Trim() ?? string.Empty,
            SessionId = string.IsNullOrEmpty(request.SessionId) ? null : request.SessionId,
            SessionPhase = Enum.TryParse<SessionPhase>(request.SessionPhase, true, out var sp) ? sp : null,
            IsReduceOnly = request.IsReduceOnly
        };

        validationError = null;
        return true;
    }
}
