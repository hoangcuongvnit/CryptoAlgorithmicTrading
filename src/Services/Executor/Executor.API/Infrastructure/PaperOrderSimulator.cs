using CryptoTrading.Shared.DTOs;
using Executor.API.Configuration;
using Microsoft.Extensions.Options;

namespace Executor.API.Infrastructure;

public sealed class PaperOrderSimulator
{
    private readonly TradingSettings _settings;
    private readonly PriceReferenceRepository _priceReferenceRepository;

    public PaperOrderSimulator(
        IOptions<TradingSettings> settings,
        PriceReferenceRepository priceReferenceRepository)
    {
        _settings = settings.Value;
        _priceReferenceRepository = priceReferenceRepository;
    }

    public async Task<OrderResult> ExecuteAsync(OrderRequest request, CancellationToken cancellationToken)
    {
        var referencePrice = request.Price;

        if (referencePrice <= 0)
        {
            referencePrice = await _priceReferenceRepository.GetLatestClosePriceAsync(request.Symbol, cancellationToken) ?? 0m;
        }

        if (referencePrice <= 0)
        {
            return new OrderResult
            {
                Symbol = request.Symbol,
                Side = request.Side,
                Success = false,
                ErrorMessage = "No valid reference price available for paper fill.",
                Timestamp = DateTime.UtcNow,
                IsPaperTrade = true
            };
        }

        var slippageFactor = _settings.PaperSlippageBps / 10_000m;
        var filledPrice = request.Side == OrderSide.Buy
            ? referencePrice * (1m + slippageFactor)
            : referencePrice * (1m - slippageFactor);

        return new OrderResult
        {
            OrderId = Guid.NewGuid().ToString("N"),
            Symbol = request.Symbol,
            Side = request.Side,
            Success = true,
            FilledPrice = decimal.Round(filledPrice, 8, MidpointRounding.AwayFromZero),
            FilledQty = request.Quantity,
            Timestamp = DateTime.UtcNow,
            IsPaperTrade = true
        };
    }
}
