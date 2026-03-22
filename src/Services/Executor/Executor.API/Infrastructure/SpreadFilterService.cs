using Binance.Net.Interfaces.Clients;
using Executor.API.Configuration;
using Microsoft.Extensions.Options;

namespace Executor.API.Infrastructure;

/// <summary>
/// Phase 1.3: Pre-execution spread gate.
/// Fetches the top-of-book bid/ask from Binance and rejects orders where the spread
/// exceeds configured limits (0.2% for BTC/ETH, 0.5% for altcoins).
/// </summary>
public sealed class SpreadFilterService
{
    private readonly IBinanceRestClient _binanceClient;
    private readonly TradingSettings _settings;
    private readonly ILogger<SpreadFilterService> _logger;

    public SpreadFilterService(
        IBinanceRestClient binanceClient,
        IOptions<TradingSettings> settings,
        ILogger<SpreadFilterService> logger)
    {
        _binanceClient = binanceClient;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Checks whether the current bid/ask spread for <paramref name="symbol"/> is within limits.
    /// Returns <c>(true, null)</c> when the order may proceed; <c>(false, reason)</c> when rejected.
    /// On any exchange error the check is skipped and the order is allowed through.
    /// </summary>
    public async Task<(bool Passed, string? Reason)> CheckSpreadAsync(
        string symbol, CancellationToken ct)
    {
        if (!_settings.SpreadFilter.Enabled)
            return (true, null);

        try
        {
            var ob = await _binanceClient.SpotApi.ExchangeData.GetOrderBookAsync(
                symbol, limit: 5, ct: ct);

            if (!ob.Success || !ob.Data.Bids.Any() || !ob.Data.Asks.Any())
            {
                _logger.LogWarning(
                    "Spread check skipped for {Symbol}: order book unavailable ({Error})",
                    symbol, ob.Error?.Message);
                return (true, null);
            }

            var bid = ob.Data.Bids.First().Price;
            var ask = ob.Data.Asks.First().Price;
            var mid = (bid + ask) / 2m;
            var spread = mid > 0 ? (ask - bid) / mid : 0m;

            var limit = _settings.SpreadFilter.MajorSymbols.Contains(symbol)
                ? _settings.SpreadFilter.BtcEthSpreadLimit
                : _settings.SpreadFilter.AltcoinSpreadLimit;

            if (spread > limit)
            {
                var reason = $"Spread {spread:P3} > limit {limit:P3} for {symbol} (bid={bid}, ask={ask})";
                _logger.LogWarning("Order rejected by spread filter: {Reason}", reason);
                return (false, reason);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spread check failed for {Symbol}, allowing order through", symbol);
            return (true, null);
        }
    }
}
