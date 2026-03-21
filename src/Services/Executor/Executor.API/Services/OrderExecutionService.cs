using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Session;
using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Executor.API.Services;

/// <summary>
/// Core order execution logic shared by gRPC service and LiquidationOrchestrator.
/// </summary>
public sealed class OrderExecutionService
{
    private readonly TradingSettings _tradingSettings;
    private readonly PaperOrderSimulator _paperOrderSimulator;
    private readonly BinanceOrderClient _binanceOrderClient;
    private readonly OrderRepository _orderRepository;
    private readonly AuditStreamPublisher _auditStreamPublisher;
    private readonly PositionTracker _positionTracker;
    private readonly OrderExecutionMetrics _metrics;
    private readonly SessionClock _sessionClock;
    private readonly SessionSettings _sessionSettings;
    private readonly ILogger<OrderExecutionService> _logger;

    public OrderExecutionService(
        IOptions<TradingSettings> tradingSettings,
        PaperOrderSimulator paperOrderSimulator,
        BinanceOrderClient binanceOrderClient,
        OrderRepository orderRepository,
        AuditStreamPublisher auditStreamPublisher,
        PositionTracker positionTracker,
        OrderExecutionMetrics metrics,
        SessionClock sessionClock,
        IOptions<SessionSettings> sessionSettings,
        ILogger<OrderExecutionService> logger)
    {
        _tradingSettings = tradingSettings.Value;
        _paperOrderSimulator = paperOrderSimulator;
        _binanceOrderClient = binanceOrderClient;
        _orderRepository = orderRepository;
        _auditStreamPublisher = auditStreamPublisher;
        _positionTracker = positionTracker;
        _metrics = metrics;
        _sessionClock = sessionClock;
        _sessionSettings = sessionSettings.Value;
        _logger = logger;
    }

    public async Task<OrderResult> ExecuteOrderAsync(OrderRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // Stamp session if not already set
        var session = _sessionSettings.Enabled ? _sessionClock.GetCurrentSession() : null;
        var orderRequest = request with
        {
            SessionId = request.SessionId ?? session?.SessionId
        };

        _metrics.RecordOrderPlaced(orderRequest.Symbol, orderRequest.Side.ToString(), orderRequest.Quantity);

        OrderResult orderResult;
        try
        {
            orderResult = _tradingSettings.PaperTradingMode
                ? await _paperOrderSimulator.ExecuteAsync(orderRequest, ct)
                : await _binanceOrderClient.PlaceOrderAsync(orderRequest, ct);

            // Stamp session on result
            orderResult = orderResult with
            {
                SessionId = orderRequest.SessionId,
                ForcedLiquidation = orderRequest.IsReduceOnly && orderRequest.StrategyName == "LiquidationOrchestrator",
                LiquidationReason = orderRequest.IsReduceOnly && orderRequest.StrategyName == "LiquidationOrchestrator"
                    ? LiquidationReason.Deadline
                    : LiquidationReason.None
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
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = _tradingSettings.PaperTradingMode,
                SessionId = orderRequest.SessionId
            };
        }

        stopwatch.Stop();
        _metrics.RecordOrderLatency(stopwatch.Elapsed.TotalMilliseconds, orderRequest.Symbol);

        if (orderResult.Success)
        {
            _metrics.RecordOrderFilled(orderRequest.Symbol, orderResult.FilledQty, orderResult.FilledPrice, orderResult.IsPaperTrade);
            _positionTracker.OnOrderFilled(orderRequest, orderResult);
        }

        try
        {
            await _orderRepository.PersistAsync(orderRequest, orderResult, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order persistence failed for {Symbol}", orderRequest.Symbol);
        }

        try
        {
            await _auditStreamPublisher.PublishAsync(orderRequest, orderResult);
            _metrics.RecordAuditEventPublished();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit stream publish failed for {Symbol}", orderRequest.Symbol);
        }

        return orderResult;
    }
}
