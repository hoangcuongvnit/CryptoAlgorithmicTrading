using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Microsoft.Extensions.Options;

namespace Executor.API.Services;

public sealed record CloseAllTarget(string Symbol, decimal Quantity, string Source);

public sealed record CloseAllLeftoverAsset(
    string Asset,
    string? SymbolCandidate,
    decimal Quantity,
    decimal EstimatedUsdtValue,
    string Reason);

public sealed class CloseAllDiscoveryResult
{
    public bool DiscoveryEnabled { get; init; }
    public int DiscoveredCandidatesCount { get; init; }
    public List<CloseAllTarget> Targets { get; init; } = [];
    public List<CloseAllLeftoverAsset> PreCloseLeftovers { get; init; } = [];
}

public sealed class CloseAllDiscoveryService
{
    private readonly BinanceRestClientProvider _clientProvider;
    private readonly CloseAllDiscoverySettings _settings;
    private readonly ILogger<CloseAllDiscoveryService> _logger;

    public CloseAllDiscoveryService(
        BinanceRestClientProvider clientProvider,
        IOptions<TradingSettings> tradingSettings,
        ILogger<CloseAllDiscoveryService> logger)
    {
        _clientProvider = clientProvider;
        _settings = tradingSettings.Value.CloseAllDiscovery;
        _logger = logger;
    }

    public async Task<CloseAllDiscoveryResult> BuildClosePlanAsync(
        IReadOnlyList<PositionTracker.OpenPosition> localPositions,
        CancellationToken ct)
    {
        var targetsBySymbol = new Dictionary<string, CloseAllTarget>(StringComparer.OrdinalIgnoreCase);

        foreach (var pos in localPositions)
        {
            if (pos.Quantity <= 0m)
                continue;

            targetsBySymbol[pos.Symbol] = new CloseAllTarget(pos.Symbol, pos.Quantity, "LOCAL");
        }

        if (!_settings.DiscoveryEnabled)
        {
            return new CloseAllDiscoveryResult
            {
                DiscoveryEnabled = false,
                Targets = targetsBySymbol.Values.OrderBy(x => x.Symbol).ToList()
            };
        }

        var leftovers = new List<CloseAllLeftoverAsset>();
        var discoveredCount = 0;

        var snapshot = await _clientProvider.Current.SpotApi.Account.GetAccountInfoAsync(ct: ct);
        if (!snapshot.Success || snapshot.Data is null)
        {
            _logger.LogWarning("Close-all discovery failed to read Binance account snapshot: {Error}", snapshot.Error?.Message);
            return new CloseAllDiscoveryResult
            {
                DiscoveryEnabled = true,
                Targets = targetsBySymbol.Values.OrderBy(x => x.Symbol).ToList(),
                PreCloseLeftovers = leftovers
            };
        }

        var quoteAsset = NormalizeQuoteAsset(_settings.QuoteAsset);
        var excludedAssets = new HashSet<string>(
            _settings.ExcludedAssets.Select(x => x.Trim().ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var balance in snapshot.Data.Balances)
        {
            var asset = balance.Asset?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(asset))
                continue;

            var quantity = balance.Total;
            if (quantity <= 0m)
                continue;

            if (excludedAssets.Contains(asset))
                continue;

            var symbol = asset + quoteAsset;
            if (!await IsUsdtPairTradeableAsync(symbol, ct))
            {
                leftovers.Add(new CloseAllLeftoverAsset(
                    Asset: asset,
                    SymbolCandidate: symbol,
                    Quantity: quantity,
                    EstimatedUsdtValue: 0m,
                    Reason: "NO_USDT_PAIR"));
                continue;
            }

            var (estimatedUsdt, hasPrice) = await TryEstimateUsdtValueAsync(asset, symbol, quantity, quoteAsset, ct);
            if (hasPrice && estimatedUsdt < Math.Max(0m, _settings.DiscoveryMinUsdtValue))
                continue;

            discoveredCount++;

            if (targetsBySymbol.TryGetValue(symbol, out var existing))
            {
                if (quantity > existing.Quantity)
                {
                    targetsBySymbol[symbol] = existing with
                    {
                        Quantity = quantity,
                        Source = existing.Source == "LOCAL" ? "MERGED" : existing.Source
                    };
                }
                else if (existing.Source == "LOCAL")
                {
                    targetsBySymbol[symbol] = existing with { Source = "MERGED" };
                }
            }
            else
            {
                targetsBySymbol[symbol] = new CloseAllTarget(symbol, quantity, "DISCOVERED");
            }
        }

        return new CloseAllDiscoveryResult
        {
            DiscoveryEnabled = true,
            DiscoveredCandidatesCount = discoveredCount,
            Targets = targetsBySymbol.Values.OrderBy(x => x.Symbol).ToList(),
            PreCloseLeftovers = leftovers
        };
    }

    public async Task<List<CloseAllLeftoverAsset>> VerifyLeftoversAsync(
        IEnumerable<string> attemptedSymbols,
        IEnumerable<CloseAllLeftoverAsset> preCloseLeftovers,
        CancellationToken ct)
    {
        var leftovers = new List<CloseAllLeftoverAsset>(preCloseLeftovers);
        var attempted = attemptedSymbols
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (attempted.Count == 0)
            return leftovers;

        var snapshot = await _clientProvider.Current.SpotApi.Account.GetAccountInfoAsync(ct: ct);
        if (!snapshot.Success || snapshot.Data is null)
        {
            leftovers.Add(new CloseAllLeftoverAsset(
                Asset: "UNKNOWN",
                SymbolCandidate: null,
                Quantity: 0m,
                EstimatedUsdtValue: 0m,
                Reason: "VERIFY_SNAPSHOT_UNAVAILABLE"));
            return leftovers;
        }

        var balancesByAsset = snapshot.Data.Balances
            .Where(b => b.Total > 0m)
            .ToDictionary(b => b.Asset.ToUpperInvariant(), b => b.Total, StringComparer.OrdinalIgnoreCase);

        var quoteAsset = NormalizeQuoteAsset(_settings.QuoteAsset);

        foreach (var symbol in attempted)
        {
            var baseAsset = TryExtractBaseAsset(symbol, quoteAsset);
            if (baseAsset is null)
                continue;

            if (!balancesByAsset.TryGetValue(baseAsset, out var qty) || qty <= 0m)
                continue;

            var (estimatedUsdt, hasPrice) = await TryEstimateUsdtValueAsync(baseAsset, symbol, qty, quoteAsset, ct);
            var threshold = Math.Max(0m, _settings.VerificationMinUsdtValue);
            var include = hasPrice
                ? estimatedUsdt >= threshold
                : qty > 0m;

            if (!include)
                continue;

            leftovers.Add(new CloseAllLeftoverAsset(
                Asset: baseAsset,
                SymbolCandidate: symbol,
                Quantity: qty,
                EstimatedUsdtValue: estimatedUsdt,
                Reason: hasPrice ? "STILL_PRESENT" : "PRICE_UNAVAILABLE"));
        }

        return leftovers
            .GroupBy(x => $"{x.Asset}|{x.SymbolCandidate}|{x.Reason}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.EstimatedUsdtValue).ThenByDescending(x => x.Quantity).First())
            .OrderByDescending(x => x.EstimatedUsdtValue)
            .ThenByDescending(x => x.Quantity)
            .ToList();
    }

    private async Task<bool> IsUsdtPairTradeableAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var info = await _clientProvider.Current.SpotApi.ExchangeData.GetExchangeInfoAsync(symbol, ct);
            if (!info.Success || info.Data is null)
                return false;

            var entry = info.Data.Symbols.FirstOrDefault();
            return entry is not null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(decimal EstimatedUsdt, bool HasPrice)> TryEstimateUsdtValueAsync(
        string asset,
        string symbol,
        decimal quantity,
        string quoteAsset,
        CancellationToken ct)
    {
        if (string.Equals(asset, quoteAsset, StringComparison.OrdinalIgnoreCase))
            return (quantity, true);

        try
        {
            var avg = await _clientProvider.Current.SpotApi.ExchangeData.GetCurrentAvgPriceAsync(symbol, ct);
            if (!avg.Success || avg.Data.Price <= 0m)
                return (0m, false);

            return (quantity * avg.Data.Price, true);
        }
        catch
        {
            return (0m, false);
        }
    }

    private static string NormalizeQuoteAsset(string? quoteAsset)
    {
        if (string.IsNullOrWhiteSpace(quoteAsset))
            return "USDT";

        return quoteAsset.Trim().ToUpperInvariant();
    }

    private static string? TryExtractBaseAsset(string symbol, string quoteAsset)
    {
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(quoteAsset))
            return null;

        return symbol.EndsWith(quoteAsset, StringComparison.OrdinalIgnoreCase)
            ? symbol[..^quoteAsset.Length]
            : null;
    }
}
