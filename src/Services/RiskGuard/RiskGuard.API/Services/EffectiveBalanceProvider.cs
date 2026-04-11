using Microsoft.Extensions.Options;
using RiskGuard.API.Configuration;

namespace RiskGuard.API.Services;

public sealed record EffectiveBalanceSnapshot(
    bool IsAvailable,
    decimal Balance,
    string Environment,
    string Source,
    DateTime AsOfUtc,
    bool IsStale,
    bool IsFallback,
    string? Error);

public interface IEffectiveBalanceProvider
{
    Task<EffectiveBalanceSnapshot> GetEffectiveBalanceAsync(string? preferredEnvironment = null, CancellationToken ct = default);
}

public sealed class EffectiveBalanceProvider : IEffectiveBalanceProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RiskSettings _settings;
    private readonly ILogger<EffectiveBalanceProvider> _logger;

    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly Dictionary<string, CacheEntry> _cacheByEnvironment = new(StringComparer.OrdinalIgnoreCase);

    public EffectiveBalanceProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<RiskSettings> settings,
        ILogger<EffectiveBalanceProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<EffectiveBalanceSnapshot> GetEffectiveBalanceAsync(string? preferredEnvironment = null, CancellationToken ct = default)
    {
        var cacheKey = NormalizeEnvironment(preferredEnvironment);
        var now = DateTime.UtcNow;
        if (_cacheByEnvironment.TryGetValue(cacheKey, out var entry) && now < entry.ValidUntilUtc)
        {
            return entry.Snapshot;
        }

        await _cacheLock.WaitAsync(ct);
        try
        {
            now = DateTime.UtcNow;
            if (_cacheByEnvironment.TryGetValue(cacheKey, out entry) && now < entry.ValidUntilUtc)
            {
                return entry.Snapshot;
            }

            var resolved = await ResolveAsync(cacheKey, ct);
            var ttlSeconds = Math.Max(1, _settings.BalanceCacheTtlSeconds);
            _cacheByEnvironment[cacheKey] = new CacheEntry(resolved, now.AddSeconds(ttlSeconds));
            return resolved;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<EffectiveBalanceSnapshot> ResolveAsync(string preferredEnvironment, CancellationToken ct)
    {
        var requestedEnvironment = preferredEnvironment == "MAINNET" ? "MAINNET" : "TESTNET";
        return await ResolveLedgerBalanceAsync(requestedEnvironment, ct);
    }

    private async Task<EffectiveBalanceSnapshot> ResolveLedgerBalanceAsync(string environment, CancellationToken ct)
    {
        try
        {
            var financialLedger = _httpClientFactory.CreateClient("financialledger");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(200, _settings.BalanceLookupTimeoutMs)));

            var path = $"/api/ledger/balance/effective?environment={environment}&baseCurrency={Uri.EscapeDataString(_settings.BalanceBaseCurrency)}";
            var response = await financialLedger.GetFromJsonAsync<LedgerEffectiveBalanceResponse>(
                path,
                cts.Token);

            if (response is not null && response.Available && response.Balance.HasValue)
            {
                var resolvedEnvironment = NormalizeEnvironment(response.Environment);
                return new EffectiveBalanceSnapshot(
                    IsAvailable: true,
                    Balance: Math.Max(0m, response.Balance.Value),
                    Environment: resolvedEnvironment == "UNKNOWN" ? environment : resolvedEnvironment,
                    Source: string.IsNullOrWhiteSpace(response.Source) ? "FINANCIAL_LEDGER" : response.Source!,
                    AsOfUtc: response.AsOfUtc ?? DateTime.UtcNow,
                    IsStale: false,
                    IsFallback: false,
                    Error: null);
            }

            return BuildFailure(response?.Detail ?? "FinancialLedger effective balance is unavailable.", environment, "FINANCIAL_LEDGER");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve {Environment} effective balance from FinancialLedger", environment);
            return BuildFailure($"Balance lookup failed: {ex.Message}");
        }
    }

    private EffectiveBalanceSnapshot BuildFailure(string reason, string environment = "UNKNOWN", string source = "UNAVAILABLE")
    {
        if (_settings.AllowVirtualBalanceFallback && _settings.VirtualAccountBalance > 0)
        {
            return new EffectiveBalanceSnapshot(
                IsAvailable: true,
                Balance: _settings.VirtualAccountBalance,
                Environment: environment,
                Source: "VIRTUAL_FALLBACK",
                AsOfUtc: DateTime.UtcNow,
                IsStale: true,
                IsFallback: true,
                Error: reason);
        }

        return new EffectiveBalanceSnapshot(
            IsAvailable: false,
            Balance: 0m,
            Environment: environment,
            Source: source,
            AsOfUtc: DateTime.UtcNow,
            IsStale: true,
            IsFallback: false,
            Error: reason);
    }

    private static string NormalizeEnvironment(string? environment)
    {
        if (string.Equals(environment, "TESTNET", StringComparison.OrdinalIgnoreCase))
            return "TESTNET";
        if (string.Equals(environment, "MAINNET", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environment, "LIVE", StringComparison.OrdinalIgnoreCase))
            return "MAINNET";

        return "UNKNOWN";
    }

    private sealed class LedgerEffectiveBalanceResponse
    {
        public string? Environment { get; set; }
        public string? Source { get; set; }
        public bool Available { get; set; }
        public decimal? Balance { get; set; }
        public DateTime? AsOfUtc { get; set; }
        public string? Detail { get; set; }
    }

    private sealed record CacheEntry(EffectiveBalanceSnapshot Snapshot, DateTime ValidUntilUtc);
}
