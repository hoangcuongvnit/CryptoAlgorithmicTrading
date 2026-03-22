using Analyzer.Worker.Infrastructure;
using System.Net.Http.Json;

namespace Analyzer.Worker.Workers;

/// <summary>
/// Phase 2.4: Background worker that fetches perpetual futures funding rates from
/// Binance every hour and stores them in <see cref="FundingRateCache"/>.
/// Uses the public endpoint — no API key required.
/// </summary>
public sealed class FundingRateFetcherWorker : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromHours(1);

    private readonly FundingRateCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FundingRateFetcherWorker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public FundingRateFetcherWorker(
        FundingRateCache cache,
        IConfiguration configuration,
        ILogger<FundingRateFetcherWorker> logger,
        IHttpClientFactory httpClientFactory)
    {
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fetch immediately on startup, then every hour
        await FetchAllAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await FetchAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Funding rate fetch cycle failed");
            }
        }
    }

    private async Task FetchAllAsync(CancellationToken ct)
    {
        var symbols = _configuration.GetSection("Trading:Symbols").Get<List<string>>()
            ?? _configuration.GetSection("Analyzer:Symbols").Get<List<string>>()
            ?? [];

        // If no explicit list, use the common BTC/ETH defaults
        if (symbols.Count == 0)
            symbols = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT"];

        foreach (var symbol in symbols)
        {
            try
            {
                await FetchAsync(symbol, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Funding rate fetch failed for {Symbol}", symbol);
            }
        }
    }

    private async Task FetchAsync(string symbol, CancellationToken ct)
    {
        // Binance USDⓈ-M Futures premium index — no auth required
        var url = $"https://fapi.binance.com/fapi/v1/premiumIndex?symbol={symbol}";
        var http = _httpClientFactory.CreateClient("BinanceFunding");
        var result = await http.GetFromJsonAsync<BinancePremiumIndex>(url, ct);
        if (result is null) return;

        if (decimal.TryParse(result.LastFundingRate, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var rate))
        {
            _cache.Set(symbol, rate);
            _logger.LogDebug("Funding rate {Symbol}={Rate:P4}", symbol, rate);
        }
    }

    private sealed record BinancePremiumIndex(string Symbol, string LastFundingRate);
}
