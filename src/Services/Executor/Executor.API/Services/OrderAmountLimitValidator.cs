using CryptoTrading.Shared.DTOs;
using Executor.API.Infrastructure;

namespace Executor.API.Services;

public sealed record OrderAmountValidationResult(
    bool Passed,
    string? ErrorMessage,
    TradingErrorCode ErrorCode,
    decimal EffectivePrice,
    decimal OrderAmount,
    decimal MinOrderAmount,
    decimal MaxOrderAmount);

public sealed class OrderAmountLimitValidator
{
    private readonly OrderAmountLimitStore _limits;
    private readonly PriceReferenceRepository _priceReferenceRepository;
    private readonly ILogger<OrderAmountLimitValidator> _logger;

    public OrderAmountLimitValidator(
        OrderAmountLimitStore limits,
        PriceReferenceRepository priceReferenceRepository,
        ILogger<OrderAmountLimitValidator> logger)
    {
        _limits = limits;
        _priceReferenceRepository = priceReferenceRepository;
        _logger = logger;
    }

    public async Task<OrderAmountValidationResult> ValidateAsync(OrderRequest request, CancellationToken ct)
    {
        var snapshot = _limits.Current;

        // Min/max notional limits are entry guards; only BUY orders are constrained.
        if (request.Side != OrderSide.Buy)
        {
            return new OrderAmountValidationResult(true, null, TradingErrorCode.None, 0m, 0m, snapshot.MinOrderAmount, snapshot.MaxOrderAmount);
        }

        // Reduce-only orders (close-all/liquidation) must be allowed to flatten
        // positions even when their notional is outside normal entry bounds.
        if (request.IsReduceOnly)
        {
            return new OrderAmountValidationResult(true, null, TradingErrorCode.None, 0m, 0m, snapshot.MinOrderAmount, snapshot.MaxOrderAmount);
        }

        var effectivePrice = request.Price > 0
            ? request.Price
            : await _priceReferenceRepository.GetLatestClosePriceAsync(request.Symbol, ct) ?? 0m;

        if (effectivePrice <= 0)
        {
            var errorMessage = $"Unable to resolve a reference price for {request.Symbol} to validate order amount limits.";
            _logger.LogWarning(errorMessage);
            return new OrderAmountValidationResult(false, errorMessage, TradingErrorCode.NoReferencePrice, 0m, 0m, snapshot.MinOrderAmount, snapshot.MaxOrderAmount);
        }

        var orderAmount = request.Quantity * effectivePrice;

        if (orderAmount < snapshot.MinOrderAmount)
        {
            var errorMessage = $"Order amount {orderAmount:0.########} is below configured minimum {snapshot.MinOrderAmount:0.########} for {request.Symbol}";
            _logger.LogWarning(errorMessage);
            return new OrderAmountValidationResult(false, errorMessage, TradingErrorCode.InvalidOrderParameters, effectivePrice, orderAmount, snapshot.MinOrderAmount, snapshot.MaxOrderAmount);
        }

        if (orderAmount > snapshot.MaxOrderAmount)
        {
            var errorMessage = $"Order amount {orderAmount:0.########} exceeds configured maximum {snapshot.MaxOrderAmount:0.########} for {request.Symbol}";
            _logger.LogWarning(errorMessage);
            return new OrderAmountValidationResult(false, errorMessage, TradingErrorCode.MaxNotionalExceeded, effectivePrice, orderAmount, snapshot.MinOrderAmount, snapshot.MaxOrderAmount);
        }

        return new OrderAmountValidationResult(true, null, TradingErrorCode.None, effectivePrice, orderAmount, snapshot.MinOrderAmount, snapshot.MaxOrderAmount);
    }
}