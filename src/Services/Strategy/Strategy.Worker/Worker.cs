using System.Text.Json;
using CryptoTrading.Executor.Grpc;
using CryptoTrading.RiskGuard.Grpc;
using CryptoTrading.Shared.Constants;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;
using StackExchange.Redis;
using Strategy.Worker.Services;

namespace Strategy.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly RiskGuardService.RiskGuardServiceClient _riskGuardClient;
    private readonly OrderExecutorService.OrderExecutorServiceClient _executorClient;
    private readonly SignalToOrderMapper _mapper;
    private ISubscriber? _subscriber;

    public Worker(
        ILogger<Worker> logger,
        IConnectionMultiplexer redis,
        RiskGuardService.RiskGuardServiceClient riskGuardClient,
        OrderExecutorService.OrderExecutorServiceClient executorClient,
        SignalToOrderMapper mapper)
    {
        _logger = logger;
        _redis = redis;
        _riskGuardClient = riskGuardClient;
        _executorClient = executorClient;
        _mapper = mapper;
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

        if (!_mapper.TryMap(signal, out var order) || order is null)
        {
            _logger.LogDebug("Signal for {Symbol} ignored by strategy mapper", signal.Symbol);
            return;
        }

        var riskResponse = await _riskGuardClient.ValidateOrderAsync(new ValidateOrderRequest
        {
            Symbol = order.Symbol,
            Side = order.Side.ToString(),
            Quantity = (double)order.Quantity,
            EntryPrice = (double)order.Price,
            StopLoss = (double)order.StopLoss,
            TakeProfit = (double)order.TakeProfit
        }, cancellationToken: cancellationToken);

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

        var executionReply = await _executorClient.PlaceOrderAsync(new PlaceOrderRequest
        {
            Symbol = order.Symbol,
            Side = order.Side.ToString(),
            OrderType = order.Type.ToString(),
            Quantity = (double)finalQuantity,
            Price = (double)order.Price,
            StopLoss = (double)order.StopLoss,
            TakeProfit = (double)order.TakeProfit,
            Strategy = order.StrategyName
        }, cancellationToken: cancellationToken);

        if (executionReply.Success)
        {
            _logger.LogInformation(
                "Order executed for {Symbol} | Qty={Qty} | Price={Price} | Paper={Paper}",
                order.Symbol,
                executionReply.FilledQty,
                executionReply.FilledPrice,
                executionReply.IsPaper);
            return;
        }

        _logger.LogWarning(
            "Order execution failed for {Symbol}: {Error}",
            order.Symbol,
            executionReply.ErrorMessage);
    }
}
