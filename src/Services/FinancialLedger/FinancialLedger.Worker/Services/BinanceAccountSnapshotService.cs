using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using CryptoExchange.Net.Authentication;
using FinancialLedger.Worker.Configuration;

namespace FinancialLedger.Worker.Services;

public sealed class BinanceAccountSnapshotService
{
    private static readonly string[] SupportedStableCoins =
    [
        "USDT",
        "USDC",
        "FDUSD",
        "BUSD",
        "TUSD",
        "USDP",
        "DAI",
        "PYUSD"
    ];

    private static readonly HashSet<string> SupportedStableCoinSet =
        new(SupportedStableCoins, StringComparer.OrdinalIgnoreCase);

    private readonly BinanceCredentialState _credentialState;
    private readonly ILogger<BinanceAccountSnapshotService> _logger;

    public BinanceAccountSnapshotService(
        BinanceCredentialState credentialState,
        ILogger<BinanceAccountSnapshotService> logger)
    {
        _credentialState = credentialState;
        _logger = logger;
    }

    public async Task<BinanceAccountSnapshotResponse> GetSnapshotAsync(CancellationToken ct)
    {
        var cfg = _credentialState.GetSnapshot();
        var isTestnet = cfg.UseTestnet;

        if (string.IsNullOrWhiteSpace(cfg.ActiveApiKey) || string.IsNullOrWhiteSpace(cfg.ActiveApiSecret))
        {
            return new BinanceAccountSnapshotResponse
            {
                IsTestnet = isTestnet,
                Unavailable = true,
                Detail = "Binance API key/secret not configured for FinancialLedger.",
            };
        }

        var attempts = Math.Max(1, cfg.MaxRetries);
        var delayMs = Math.Max(200, cfg.RetryDelayMs);
        var timeoutSeconds = Math.Clamp(cfg.RequestTimeoutSeconds, 3, 30);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var client = CreateClient(cfg);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                var accountResult = await client.SpotApi.Account.GetAccountInfoAsync(ct: linkedCts.Token);
                if (!accountResult.Success || accountResult.Data is null)
                {
                    var detail = accountResult.Error?.Message ?? "Unknown Binance account query error.";
                    if (attempt == attempts)
                    {
                        return new BinanceAccountSnapshotResponse
                        {
                            IsTestnet = isTestnet,
                            Unavailable = true,
                            Detail = $"Binance account query failed: {detail}",
                        };
                    }

                    _logger.LogInformation(
                        "BinanceAccountSnapshotService: account query failed (attempt {Attempt}/{Max}). Retrying in {DelayMs}ms. Error={Error}",
                        attempt, attempts, delayMs, detail);
                }
                else
                {
                    return BuildSnapshot(accountResult.Data.Balances, isTestnet);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                if (attempt == attempts)
                {
                    return new BinanceAccountSnapshotResponse
                    {
                        IsTestnet = isTestnet,
                        Unavailable = true,
                        Detail = "Timed out while querying Binance spot account.",
                    };
                }

                _logger.LogInformation(
                    "BinanceAccountSnapshotService: timeout on attempt {Attempt}/{Max}. Retrying in {DelayMs}ms.",
                    attempt, attempts, delayMs);
            }
            catch (Exception ex)
            {
                if (attempt == attempts)
                {
                    return new BinanceAccountSnapshotResponse
                    {
                        IsTestnet = isTestnet,
                        Unavailable = true,
                        Detail = $"Failed to fetch Binance spot account: {ex.Message}",
                    };
                }

                _logger.LogWarning(ex,
                    "BinanceAccountSnapshotService: unexpected error on attempt {Attempt}/{Max}. Retrying in {DelayMs}ms.",
                    attempt, attempts, delayMs);
            }

            await Task.Delay(delayMs, ct);
        }

        return new BinanceAccountSnapshotResponse
        {
            IsTestnet = isTestnet,
            Unavailable = true,
            Detail = "Binance spot account is unavailable.",
        };
    }

    private BinanceAccountSnapshotResponse BuildSnapshot(IEnumerable<object> balances, bool isTestnet)
    {
        var stableCoinMap = SupportedStableCoins.ToDictionary(
            coin => coin,
            coin => new StableCoinBalanceItem
            {
                Asset = coin,
                Free = 0m,
                Locked = 0m,
                Total = 0m,
            },
            StringComparer.OrdinalIgnoreCase);

        foreach (var balance in balances)
        {
            var asset = GetStringProperty(balance, "Asset")?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(asset))
            {
                continue;
            }

            var free = GetDecimalProperty(balance, "Free");
            var locked = GetDecimalProperty(balance, "Locked");
            var total = GetDecimalProperty(balance, "Total");
            if (total <= 0m)
            {
                var available = GetDecimalProperty(balance, "Available");
                if (available > 0m)
                {
                    free = available;
                }

                total = Math.Max(0m, free + locked);
            }

            // Some Binance payloads expose Total + Locked but omit Free.
            if (free <= 0m && total > 0m)
            {
                var available = GetDecimalProperty(balance, "Available");
                if (available > 0m)
                {
                    free = available;
                }
                else
                {
                    free = Math.Max(0m, total - locked);
                }
            }

            if (total <= 0m)
            {
                continue;
            }

            if (!SupportedStableCoinSet.Contains(asset))
            {
                continue;
            }

            stableCoinMap[asset] = new StableCoinBalanceItem
            {
                Asset = asset,
                Free = free,
                Locked = locked,
                Total = total,
            };

        }

        var stableCoinBalances = SupportedStableCoins
            .Select(coin => stableCoinMap[coin])
            .ToArray();
        var stableCoinTotal = stableCoinBalances.Sum(item => item.Total);
        var usdtRow = stableCoinMap["USDT"];

        return new BinanceAccountSnapshotResponse
        {
            IsTestnet = isTestnet,
            AsOfUtc = DateTime.UtcNow,
            UsdtFree = usdtRow.Free,
            UsdtLocked = usdtRow.Locked,
            UsdtTotal = usdtRow.Total,
            SupportedStableCoins = SupportedStableCoins,
            StableCoinBalances = stableCoinBalances,
            StableCoinTotal = stableCoinTotal,
            Detail = "Stablecoin balances resolved from Binance Spot account."
        };
    }

    private static IBinanceRestClient CreateClient(BinanceAccountSettings cfg)
        => new BinanceRestClient(options =>
        {
            options.Environment = cfg.UseTestnet ? BinanceEnvironment.Testnet : BinanceEnvironment.Live;
            options.ApiCredentials = new ApiCredentials(cfg.ActiveApiKey, cfg.ActiveApiSecret);
        });

    private static string? GetStringProperty(object source, string propertyName)
    {
        var prop = source.GetType().GetProperty(propertyName);
        return prop?.GetValue(source)?.ToString();
    }

    private static decimal GetDecimalProperty(object source, string propertyName)
    {
        var prop = source.GetType().GetProperty(propertyName);
        var raw = prop?.GetValue(source);
        if (raw is null)
        {
            return 0m;
        }

        try
        {
            return Convert.ToDecimal(raw);
        }
        catch
        {
            return 0m;
        }
    }
}

public sealed class BinanceAccountSnapshotResponse
{
    public bool IsTestnet { get; init; }
    public DateTime? AsOfUtc { get; init; }
    public bool Unavailable { get; init; }
    public string? Detail { get; init; }
    public decimal UsdtFree { get; init; }
    public decimal UsdtLocked { get; init; }
    public decimal UsdtTotal { get; init; }
    public IReadOnlyList<string> SupportedStableCoins { get; init; } = [];
    public IReadOnlyList<StableCoinBalanceItem> StableCoinBalances { get; init; } = [];
    public decimal StableCoinTotal { get; init; }
}

public sealed class StableCoinBalanceItem
{
    public string Asset { get; init; } = string.Empty;
    public decimal Free { get; init; }
    public decimal Locked { get; init; }
    public decimal Total { get; init; }
}
