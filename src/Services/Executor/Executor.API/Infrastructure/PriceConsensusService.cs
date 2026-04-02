using Executor.API.Configuration;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Executor.API.Infrastructure;

/// <summary>
/// Phase 3.2: Validates price consensus across multiple exchanges before order execution.
/// Currently supports Binance (via Binance.Net) + Bybit (public REST — no auth required).
/// An order is allowed through only when ≥ 2 exchanges agree within the configured threshold.
/// The check is skipped (fail-open) when <see cref="ConsensusPricingSettings.Enabled"/> is false
/// or when exchange queries fail due to latency / unavailability.
/// </summary>
public sealed class PriceConsensusService
{
    private readonly BinanceRestClientProvider _clientProvider;
    private readonly ConsensusPricingSettings _settings;
    private readonly ILogger<PriceConsensusService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public PriceConsensusService(
        BinanceRestClientProvider clientProvider,
        IOptions<TradingSettings> settings,
        ILogger<PriceConsensusService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _clientProvider = clientProvider;
        _settings = settings.Value.ConsensusPricing;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Checks whether at least 2 exchanges agree on the price of <paramref name="symbol"/>
    /// within <see cref="ConsensusPricingSettings.PriceAgreementThreshold"/>.
    /// Returns <c>(true, medianPrice, null)</c> on success or <c>(false, 0, reason)</c> on rejection.
    /// </summary>
    public async Task<(bool Passed, decimal ConsensusPrice, string? Reason)> ValidateAsync(
        string symbol, CancellationToken ct)
    {
        if (!_settings.Enabled)
            return (true, 0m, null);

        var prices = new List<(string Exchange, decimal Price)>();

        // Binance
        try
        {
            var result = await _clientProvider.Current.SpotApi.ExchangeData.GetCurrentAvgPriceAsync(symbol, ct);
            if (result.Success && result.Data.Price > 0)
                prices.Add(("Binance", result.Data.Price));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Binance price fetch failed for {Symbol}", symbol);
        }

        // Bybit (public endpoint — no auth)
        try
        {
            var bybitPrice = await GetBybitPriceAsync(symbol, ct);
            if (bybitPrice > 0)
                prices.Add(("Bybit", bybitPrice));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bybit price fetch failed for {Symbol}", symbol);
        }

        // Need at least 2 prices to form a consensus
        if (prices.Count < 2)
        {
            _logger.LogWarning(
                "Consensus check skipped for {Symbol}: only {Count} exchange(s) responded", symbol, prices.Count);
            return (true, 0m, null);  // fail-open when data unavailable
        }

        var sorted = prices.OrderBy(p => p.Price).ToList();
        var median = sorted[sorted.Count / 2].Price;

        int agreeCount = prices.Count(p =>
            Math.Abs(p.Price - median) / median <= _settings.PriceAgreementThreshold);

        if (agreeCount < 2)
        {
            var detail = string.Join(", ", prices.Select(p => $"{p.Exchange}={p.Price:F2}"));
            var reason = $"No consensus for {symbol}: prices diverge beyond {_settings.PriceAgreementThreshold:P1} ({detail})";
            _logger.LogWarning("Order rejected by consensus check: {Reason}", reason);
            return (false, 0m, reason);
        }

        _logger.LogDebug(
            "Consensus OK for {Symbol}: median={Median:F2} from [{Prices}]",
            symbol, median, string.Join(", ", prices.Select(p => $"{p.Exchange}={p.Price:F2}")));

        return (true, median, null);
    }

    private async Task<decimal> GetBybitPriceAsync(string symbol, CancellationToken ct)
    {
        var url = $"https://api.bybit.com/v5/market/tickers?category=spot&symbol={symbol}";
        var http = _httpClientFactory.CreateClient("BybitConsensus");
        var response = await http.GetFromJsonAsync<BybitTickerResponse>(url, ct);
        var item = response?.Result?.List?.FirstOrDefault();
        return item is not null && decimal.TryParse(item.LastPrice,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0m;
    }

    // Minimal Bybit response DTOs
    private sealed record BybitTickerResponse(BybitTickerResult? Result);
    private sealed record BybitTickerResult(List<BybitTickerItem>? List);
    private sealed record BybitTickerItem(string Symbol, string LastPrice);

}
