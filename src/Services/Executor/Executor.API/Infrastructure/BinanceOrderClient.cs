using Binance.Net.Enums;
using CryptoTrading.Shared.DTOs;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Executor.API.Infrastructure;

public sealed class BinanceOrderClient
{
    private readonly BinanceRestClientProvider _clientProvider;
    private readonly PriceReferenceRepository _priceReferenceRepository;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<BinanceOrderClient> _logger;

    private sealed record SymbolFilters(decimal StepSize, decimal MinQty, decimal MinNotional);
    private sealed record CachedSymbolFilters(SymbolFilters Filters, DateTime ExpiresAtUtc);
    private sealed record SymbolFiltersCacheEntry(decimal StepSize, decimal MinQty, decimal MinNotional, DateTime UpdatedAtUtc);
    private sealed record ExchangeRejectionSnapshot(
        string Symbol,
        string Rule,
        decimal AttemptedQty,
        decimal AttemptedPrice,
        decimal AttemptedNotional,
        decimal RequiredMinNotional,
        decimal SuggestedQty,
        string LastError,
        DateTime CreatedAtUtc);
    private readonly ConcurrentDictionary<string, CachedSymbolFilters> _filterCache = new();
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromHours(12);

    public BinanceOrderClient(
        BinanceRestClientProvider clientProvider,
        PriceReferenceRepository priceReferenceRepository,
        IConnectionMultiplexer redis,
        ILogger<BinanceOrderClient> logger)
    {
        _clientProvider = clientProvider;
        _priceReferenceRepository = priceReferenceRepository;
        _redis = redis;
        _logger = logger;
    }

    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        // Do NOT wrap order placement in a retry pipeline — a lost ACK would create duplicate orders.
        return PlaceOrderCoreAsync(request, cancellationToken);
    }

    private async Task<SymbolFilters> GetSymbolFiltersAsync(string symbol, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (_filterCache.TryGetValue(symbol, out var cached) && cached.ExpiresAtUtc > now)
        {
            return cached.Filters;
        }

        var redisCached = await TryGetRedisCachedFiltersAsync(symbol);
        if (redisCached is not null)
        {
            _filterCache[symbol] = new CachedSymbolFilters(redisCached, now.Add(_cacheTtl));
            return redisCached;
        }

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
        var minNotional = symbolInfo.MinNotionalFilter?.MinNotional ?? TryReadDecimalProperty(symbolInfo, "NotionalFilter", "MinNotional");

        var filters = new SymbolFilters(stepSize, minQty, minNotional);
        await CacheFiltersAsync(symbol, filters);
        _logger.LogDebug(
            "Cached filters for {Symbol}: StepSize={StepSize}, MinQty={MinQty}, MinNotional={MinNotional}",
            symbol, stepSize, minQty, minNotional);
        return filters;
    }

    private async Task<SymbolFilters?> TryGetRedisCachedFiltersAsync(string symbol)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = await db.StringGetAsync(GetRedisKey(symbol));
            if (json.IsNullOrEmpty)
            {
                return null;
            }

            var cached = JsonSerializer.Deserialize<SymbolFiltersCacheEntry>(json.ToString());
            return cached is null
                ? null
                : new SymbolFilters(cached.StepSize, cached.MinQty, cached.MinNotional);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read cached filters for {Symbol} from Redis", symbol);
            return null;
        }
    }

    private async Task CacheFiltersAsync(string symbol, SymbolFilters filters)
    {
        var now = DateTime.UtcNow;
        _filterCache[symbol] = new CachedSymbolFilters(filters, now.Add(_cacheTtl));

        try
        {
            var db = _redis.GetDatabase();
            var entry = new SymbolFiltersCacheEntry(filters.StepSize, filters.MinQty, filters.MinNotional, now);
            await db.StringSetAsync(GetRedisKey(symbol), JsonSerializer.Serialize(entry), _cacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist cached filters for {Symbol} to Redis", symbol);
        }
    }

    private static string GetRedisKey(string symbol) => $"exchange:filters:{symbol.ToUpperInvariant()}";

    private static decimal TryReadDecimalProperty(object? instance, string propertyName, string nestedPropertyName)
    {
        if (instance is null)
        {
            return 0m;
        }

        var property = instance.GetType().GetProperty(propertyName);
        var nestedInstance = property?.GetValue(instance);
        if (nestedInstance is null)
        {
            return 0m;
        }

        var nestedProperty = nestedInstance.GetType().GetProperty(nestedPropertyName);
        var value = nestedProperty?.GetValue(nestedInstance);
        return value is decimal decimalValue ? decimalValue : 0m;
    }

    private async Task<decimal> ResolveEffectivePriceAsync(OrderRequest request, CancellationToken ct)
    {
        if (request.Price > 0)
        {
            return request.Price;
        }

        var latestClosePrice = await _priceReferenceRepository.GetLatestClosePriceAsync(request.Symbol, ct);
        return latestClosePrice ?? 0m;
    }

    private async Task StoreRejectionSnapshotAsync(
        string symbol,
        string rule,
        decimal attemptedQty,
        decimal attemptedPrice,
        decimal attemptedNotional,
        decimal requiredMinNotional,
        decimal suggestedQty,
        string lastError)
    {
        try
        {
            var snapshot = new ExchangeRejectionSnapshot(
                symbol,
                rule,
                attemptedQty,
                attemptedPrice,
                attemptedNotional,
                requiredMinNotional,
                suggestedQty,
                lastError,
                DateTime.UtcNow);

            var db = _redis.GetDatabase();
            await db.StringSetAsync(
                $"exchange:reject:{symbol.ToUpperInvariant()}:{rule}",
                JsonSerializer.Serialize(snapshot),
                _cacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist rejection snapshot for {Symbol} {Rule}", symbol, rule);
        }
    }

    private static decimal ApplyStepSize(decimal quantity, decimal stepSize)
    {
        if (stepSize <= 0)
        {
            return quantity;
        }

        return Math.Floor(quantity / stepSize) * stepSize;
    }

    private async Task<OrderResult> PlaceOrderCoreAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        var filters = await GetSymbolFiltersAsync(request.Symbol, cancellationToken);
        var quantity = ApplyStepSize(request.Quantity, filters.StepSize);
        var effectivePrice = await ResolveEffectivePriceAsync(request, cancellationToken);

        if (quantity <= 0)
        {
            var suggestedQty = Math.Max(filters.StepSize > 0 ? filters.StepSize : 0m, filters.MinQty);
            await StoreRejectionSnapshotAsync(
                request.Symbol,
                "LOT_SIZE",
                request.Quantity,
                request.Price,
                request.Quantity * (request.Price > 0 ? request.Price : 0m),
                0m,
                suggestedQty > 0 ? suggestedQty : request.Quantity,
                $"Quantity {request.Quantity} rounds to zero with LOT_SIZE step {filters.StepSize}");

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
                IsTestnetTrade = false
            };
        }

        if (filters.MinQty > 0 && quantity < filters.MinQty)
        {
            await StoreRejectionSnapshotAsync(
                request.Symbol,
                "LOT_SIZE",
                quantity,
                effectivePrice,
                quantity * effectivePrice,
                0m,
                filters.MinQty,
                $"Quantity {quantity} is below LOT_SIZE minQty {filters.MinQty} for {request.Symbol}");

            _logger.LogWarning(
                "Order quantity {Qty} for {Symbol} is below LOT_SIZE minQty {MinQty}",
                quantity, request.Symbol, filters.MinQty);
            return new OrderResult
            {
                Symbol = request.Symbol,
                Side = request.Side,
                Success = false,
                ErrorMessage = $"Quantity {quantity} is below LOT_SIZE minQty {filters.MinQty} for {request.Symbol}",
                ErrorCode = TradingErrorCode.ExchangeRequestFailed,
                Timestamp = DateTime.UtcNow,
                IsTestnetTrade = false
            };
        }

        if (filters.MinNotional > 0 && effectivePrice > 0)
        {
            var notional = quantity * effectivePrice;
            if (notional < filters.MinNotional)
            {
                var suggestedQty = filters.StepSize > 0
                    ? Math.Ceiling(filters.MinNotional / effectivePrice / filters.StepSize) * filters.StepSize
                    : filters.MinNotional / effectivePrice;

                await StoreRejectionSnapshotAsync(
                    request.Symbol,
                    "MIN_NOTIONAL",
                    quantity,
                    effectivePrice,
                    notional,
                    filters.MinNotional,
                    suggestedQty,
                    $"Order notional {notional:F4} below MIN_NOTIONAL {filters.MinNotional} for {request.Symbol}");

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
                    IsTestnetTrade = false
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
                IsTestnetTrade = false
            };
        }

        var data = placeOrderResult.Data;
        var filledPrice = data.AverageFillPrice > 0 ? data.AverageFillPrice.Value : request.Price;
        var filledQty = data.QuantityFilled > 0 ? data.QuantityFilled : request.Quantity;

        return new OrderResult
        {
            OrderId = data.Id.ToString(),
            Symbol = request.Symbol,
            Side = request.Side,
            Success = true,
            FilledPrice = filledPrice,
            FilledQty = filledQty,
            Timestamp = DateTime.UtcNow,
            IsTestnetTrade = false
        };
    }
}
