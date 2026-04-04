using Binance.Net.Enums;
using CryptoTrading.Shared.DTOs;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Collections.Concurrent;

namespace Executor.API.Infrastructure;

public sealed class BinanceOrderClient
{
    private readonly BinanceRestClientProvider _clientProvider;
    private readonly ILogger<BinanceOrderClient> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    private sealed record SymbolFilters(decimal StepSize, decimal MinQty, decimal MinNotional);
    private readonly ConcurrentDictionary<string, SymbolFilters> _filterCache = new();

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

    private async Task<SymbolFilters> GetSymbolFiltersAsync(string symbol, CancellationToken ct)
    {
        if (_filterCache.TryGetValue(symbol, out var cached))
            return cached;

        var info = await _clientProvider.Current.SpotApi.ExchangeData.GetExchangeInfoAsync(symbol, ct);
        if (!info.Success)
        {
            _logger.LogWarning("Could not fetch exchange filters for {Symbol}, order will be sent as-is", symbol);
            return new SymbolFilters(0m, 0m, 0m);
        }

        var symbolInfo = info.Data.Symbols.FirstOrDefault();
        if (symbolInfo is null)
        {
            _logger.LogWarning("No symbol info returned for {Symbol}, order will be sent as-is", symbol);
            return new SymbolFilters(0m, 0m, 0m);
        }

        var stepSize = symbolInfo.LotSizeFilter?.StepSize ?? 0m;
        var minQty = symbolInfo.LotSizeFilter?.MinQuantity ?? 0m;
        var minNotional = symbolInfo.MinNotionalFilter?.MinNotional ?? 0m;

        var filters = new SymbolFilters(stepSize, minQty, minNotional);
        _filterCache[symbol] = filters;
        _logger.LogDebug(
            "Cached filters for {Symbol}: StepSize={StepSize}, MinQty={MinQty}, MinNotional={MinNotional}",
            symbol, stepSize, minQty, minNotional);
        return filters;
    }

    private static decimal ApplyStepSize(decimal quantity, decimal stepSize)
    {
        if (stepSize <= 0) return quantity;
        return Math.Floor(quantity / stepSize) * stepSize;
    }

    private async Task<OrderResult> PlaceOrderCoreAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        var filters = await GetSymbolFiltersAsync(request.Symbol, cancellationToken);
        var quantity = ApplyStepSize(request.Quantity, filters.StepSize);

        if (quantity <= 0)
        {
            _logger.LogWarning(
                "Order quantity {Original} for {Symbol} is below minimum step {Step}. Set DefaultOrderQuantity >= {Step}",
                request.Quantity, request.Symbol, filters.StepSize, filters.StepSize);
            return new OrderResult
            {
                Symbol = request.Symbol,
                Side = request.Side,
                Success = false,
                ErrorMessage = $"Quantity {request.Quantity} rounds to zero with LOT_SIZE step {filters.StepSize}",
                ErrorCode = TradingErrorCode.ExchangeRequestFailed,
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = false
            };
        }

        if (filters.MinNotional > 0 && request.Price > 0)
        {
            var notional = quantity * request.Price;
            if (notional < filters.MinNotional)
            {
                _logger.LogWarning(
                    "Order notional {Notional:F4} for {Symbol} is below MIN_NOTIONAL {MinNotional}. Increase quantity or use a higher price",
                    notional, request.Symbol, filters.MinNotional);
                return new OrderResult
                {
                    Symbol = request.Symbol,
                    Side = request.Side,
                    Success = false,
                    ErrorMessage = $"Order notional {notional:F4} below MIN_NOTIONAL {filters.MinNotional} for {request.Symbol}",
                    ErrorCode = TradingErrorCode.ExchangeRequestFailed,
                    Timestamp = DateTime.UtcNow,
                    IsPaperTrade = false
                };
            }
        }

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
            quantity,
            price: request.Type != OrderType.Market && request.Price > 0 ? request.Price : null,
            timeInForce: timeInForce,
            stopPrice: request.Type == OrderType.StopLimit && request.StopLoss > 0 ? request.StopLoss : null,
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
                ErrorCode = TradingErrorCode.ExchangeRequestFailed,
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = false
            };
        }

        var data = placeOrderResult.Data;
        var filledPrice = data.AverageFillPrice > 0
            ? data.AverageFillPrice ?? request.Price
            : request.Price;
        var filledQty = data.QuantityFilled > 0
            ? data.QuantityFilled
            : request.Quantity;

        return new OrderResult
        {
            OrderId = data.Id.ToString(),
            Symbol = request.Symbol,
            Side = request.Side,
            Success = true,
            FilledPrice = filledPrice,
            FilledQty = filledQty,
            Timestamp = DateTime.UtcNow,
            IsPaperTrade = false
        };
    }
}
