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
    private readonly PaperOrderSimulator _paperOrderSimulator;
    private readonly BinanceOrderClient _binanceOrderClient;
    private readonly OrderRepository _orderRepository;
    private readonly AuditStreamPublisher _auditStreamPublisher;
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
        PaperOrderSimulator paperOrderSimulator,
        BinanceOrderClient binanceOrderClient,
        OrderRepository orderRepository,
        AuditStreamPublisher auditStreamPublisher,
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
        _paperOrderSimulator = paperOrderSimulator;
        _binanceOrderClient = binanceOrderClient;
        _orderRepository = orderRepository;
        _auditStreamPublisher = auditStreamPublisher;
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
            orderResult = BuildFailureResult(request.Symbol, validationError ?? "Invalid order request.", isPaperTrade: _tradingSettings.PaperTradingMode);
            return await PersistAndReplyAsync(request, null, orderResult, context.CancellationToken, stopwatch);
        }

        if (_tradingSettings.GlobalKillSwitch)
        {
            _metrics.RecordOrderRejected(orderRequest.Symbol, "Global kill switch");
            orderResult = BuildFailureResult(orderRequest.Symbol, "Global kill switch is enabled.", _tradingSettings.PaperTradingMode);
            return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
        }

        // Shutdown/exit-only gate — block new-position orders during close-all operation
        if (_shutdownOp.IsExitOnlyMode && !request.IsReduceOnly)
        {
            _metrics.RecordOrderRejected(orderRequest.Symbol, "Shutdown exit-only mode");
            orderResult = BuildFailureResult(
                orderRequest.Symbol,
                "System is in exit-only mode. New position orders are blocked during the close-all operation.",
                _tradingSettings.PaperTradingMode);
            return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
        }

        // Recovery gate — block new-position orders until reconciliation is complete
        if (_recoveryState.IsBlockingNewOrders && !request.IsReduceOnly)
        {
            _metrics.RecordOrderRejected(orderRequest.Symbol, "Recovery mode");
            orderResult = BuildFailureResult(
                orderRequest.Symbol,
                $"System is in {_recoveryState.CurrentState} mode. New position orders blocked until recovery completes.",
                _tradingSettings.PaperTradingMode);
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
                    _tradingSettings.PaperTradingMode);
                return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
            }

            // Block new positions in liquidation/forced-flatten phases
            if (!request.IsReduceOnly && !_sessionPolicy.CanOpenNewPosition(session))
            {
                _metrics.RecordOrderRejected(orderRequest.Symbol, "Session liquidation window");
                orderResult = BuildFailureResult(orderRequest.Symbol,
                    $"Session {session.SessionId} is in {session.CurrentPhase} phase. New positions blocked.",
                    _tradingSettings.PaperTradingMode);
                return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
            }
        }

        if (_tradingSettings.AllowedSymbols.Count > 0 &&
            !_tradingSettings.AllowedSymbols.Contains(orderRequest.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            _metrics.RecordOrderRejected(orderRequest.Symbol, "Symbol not allowed");
            orderResult = BuildFailureResult(orderRequest.Symbol, $"Symbol {orderRequest.Symbol} is not allowed.", _tradingSettings.PaperTradingMode);
            return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
        }

        if (_tradingSettings.MaxNotionalPerOrder > 0 && orderRequest.Price > 0)
        {
            var notional = orderRequest.Price * orderRequest.Quantity;
            if (notional > _tradingSettings.MaxNotionalPerOrder)
            {
                _metrics.RecordOrderRejected(orderRequest.Symbol, "Notional exceeds max");
                orderResult = BuildFailureResult(
                    orderRequest.Symbol,
                    $"Order notional {notional:0.########} exceeds configured max {_tradingSettings.MaxNotionalPerOrder:0.########}.",
                    _tradingSettings.PaperTradingMode);
                return await PersistAndReplyAsync(request, orderRequest, orderResult, context.CancellationToken, stopwatch);
            }
        }

        _metrics.RecordOrderPlaced(orderRequest.Symbol, orderRequest.Side.ToString(), orderRequest.Quantity);

        try
        {
            orderResult = _tradingSettings.PaperTradingMode
                ? await _paperOrderSimulator.ExecuteAsync(orderRequest, context.CancellationToken)
                : await _binanceOrderClient.PlaceOrderAsync(orderRequest, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order execution failed unexpectedly for {Symbol}", orderRequest.Symbol);
            orderResult = BuildFailureResult(orderRequest.Symbol, ex.Message, _tradingSettings.PaperTradingMode);
        }

        if (orderResult.Success)
        {
            _metrics.RecordOrderFilled(orderRequest.Symbol, orderResult.FilledQty, orderResult.FilledPrice, orderResult.IsPaperTrade);
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

        try
        {
            await _orderRepository.PersistAsync(requestForPersistence, result, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order persistence failed for {Symbol}", requestForPersistence.Symbol);
        }

        if (result.Success)
        {
            _positionTracker.OnOrderFilled(requestForPersistence, result);
        }

        try
        {
            await _auditStreamPublisher.PublishAsync(requestForPersistence, result);
            _metrics.RecordAuditEventPublished();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit stream publish failed for {Symbol}", requestForPersistence.Symbol);
        }

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

    private static OrderResult BuildFailureResult(string symbol, string errorMessage, bool isPaperTrade)
    {
        return new OrderResult
        {
            OrderId = Guid.NewGuid().ToString("N"),
            Symbol = string.IsNullOrWhiteSpace(symbol) ? "UNKNOWN" : symbol.ToUpperInvariant(),
            Success = false,
            ErrorMessage = errorMessage,
            Timestamp = DateTime.UtcNow,
            IsPaperTrade = isPaperTrade
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
