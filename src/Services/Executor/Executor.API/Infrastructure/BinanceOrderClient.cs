using Binance.Net.Enums;
using CryptoTrading.Shared.DTOs;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Executor.API.Infrastructure;

public sealed class BinanceOrderClient
{
    private readonly BinanceRestClientProvider _clientProvider;
    private readonly ILogger<BinanceOrderClient> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    public BinanceOrderClient(
        BinanceRestClientProvider clientProvider,
        ILogger<BinanceOrderClient> logger)
    {
        _clientProvider = clientProvider;
        _logger = logger;

        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<Exception>()
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(60),
                MinimumThroughput = 4,
                ShouldHandle = new PredicateBuilder().Handle<Exception>()
            })
            .Build();
    }

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        return await _resiliencePipeline.ExecuteAsync(
            async ct => await PlaceOrderCoreAsync(request, ct),
            cancellationToken);
    }

    private async Task<OrderResult> PlaceOrderCoreAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        var side = request.Side == CryptoTrading.Shared.DTOs.OrderSide.Buy
            ? Binance.Net.Enums.OrderSide.Buy
            : Binance.Net.Enums.OrderSide.Sell;

        var type = request.Type switch
        {
            OrderType.Market => SpotOrderType.Market,
            OrderType.Limit => SpotOrderType.Limit,
            OrderType.StopLimit => SpotOrderType.StopLossLimit,
            _ => SpotOrderType.Market
        };

        var timeInForce = request.Type is OrderType.Limit or OrderType.StopLimit
            ? TimeInForce.GoodTillCanceled
            : (TimeInForce?)null;

        var placeOrderResult = await _clientProvider.Current.SpotApi.Trading.PlaceOrderAsync(
            request.Symbol,
            side,
            type,
            request.Quantity,
            price: request.Type != OrderType.Market && request.Price > 0 ? request.Price : null,
            timeInForce: timeInForce,
            stopPrice: request.StopLoss > 0 ? request.StopLoss : null,
            ct: cancellationToken);

        if (!placeOrderResult.Success)
        {
            _logger.LogWarning(
                "Live order failed for {Symbol}: {Error}",
                request.Symbol,
                placeOrderResult.Error?.Message);

            return new OrderResult
            {
                Symbol = request.Symbol,
                Side = request.Side,
                Success = false,
                ErrorMessage = placeOrderResult.Error?.Message ?? "Unknown exchange error",
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = false
            };
        }

        return new OrderResult
        {
            OrderId = placeOrderResult.Data.Id.ToString(),
            Symbol = request.Symbol,
            Side = request.Side,
            Success = true,
            FilledPrice = request.Price,
            FilledQty = request.Quantity,
            Timestamp = DateTime.UtcNow,
            IsPaperTrade = false
        };
    }
}
