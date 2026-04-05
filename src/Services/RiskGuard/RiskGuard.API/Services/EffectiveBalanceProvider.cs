using System.Net.Http.Json;
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
        if (preferredEnvironment == "TESTNET")
        {
            return await ResolveTestnetBalanceAsync(ct);
        }

        if (preferredEnvironment == "MAINNET")
        {
            return await ResolveMainnetBalanceAsync(ct);
        }

        try
        {
            var executor = _httpClientFactory.CreateClient("executor");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(200, _settings.BalanceLookupTimeoutMs)));

            var executorResult = await executor.GetFromJsonAsync<ExecutorEffectiveBalanceResponse>(
                "/api/trading/balance/effective",
                cts.Token);

            if (executorResult is null)
            {
                return BuildFailure("Executor returned an empty effective balance payload.");
            }

            var environment = NormalizeEnvironment(executorResult.Environment);
            if (environment == "TESTNET")
            {
                return await ResolveTestnetBalanceAsync(ct);
            }

            if (environment == "MAINNET")
            {
                return await ResolveMainnetBalanceAsync(ct);
            }

            return BuildFailure("Unable to determine trading environment from executor effective balance payload.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve trading environment from executor effective balance");
            return BuildFailure($"Environment lookup failed: {ex.Message}");
        }
    }

    private async Task<EffectiveBalanceSnapshot> ResolveMainnetBalanceAsync(CancellationToken ct)
    {
        try
        {
            var executor = _httpClientFactory.CreateClient("executor");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(200, _settings.BalanceLookupTimeoutMs)));

            var executorResult = await executor.GetFromJsonAsync<ExecutorEffectiveBalanceResponse>(
                "/api/trading/balance/effective",
                cts.Token);

            var reportedEnvironment = NormalizeEnvironment(executorResult?.Environment);
            if (reportedEnvironment != "MAINNET")
            {
                return BuildFailure(
                    $"Executor currently reports {reportedEnvironment}, cannot provide MAINNET reconciled balance.",
                    "MAINNET",
                    "MAINNET_RECONCILED");
            }

            if (executorResult is not null && executorResult.Available && executorResult.Balance.HasValue)
            {
                return new EffectiveBalanceSnapshot(
                    IsAvailable: true,
                    Balance: Math.Max(0m, executorResult.Balance.Value),
                    Environment: "MAINNET",
                    Source: string.IsNullOrWhiteSpace(executorResult.Source) ? "MAINNET_RECONCILED" : executorResult.Source!,
                    AsOfUtc: executorResult.AsOfUtc ?? DateTime.UtcNow,
                    IsStale: executorResult.Stale,
                    IsFallback: false,
                    Error: null);
            }

            return BuildFailure(executorResult?.Detail ?? "Mainnet reconciled balance is unavailable.", "MAINNET", "MAINNET_RECONCILED");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve MAINNET effective balance from executor");
            return BuildFailure($"Balance lookup failed: {ex.Message}");
        }
    }

    private async Task<EffectiveBalanceSnapshot> ResolveTestnetBalanceAsync(CancellationToken ct)
    {
        var financialLedger = _httpClientFactory.CreateClient("financialledger");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(200, _settings.BalanceLookupTimeoutMs)));

        var path = $"/api/ledger/balance/effective?environment=TESTNET&baseCurrency={Uri.EscapeDataString(_settings.BalanceBaseCurrency)}";
        var response = await financialLedger.GetFromJsonAsync<LedgerEffectiveBalanceResponse>(path, cts.Token);

        if (response is not null && response.Available && response.Balance.HasValue)
        {
            return new EffectiveBalanceSnapshot(
                IsAvailable: true,
                Balance: Math.Max(0m, response.Balance.Value),
                Environment: "TESTNET",
                Source: string.IsNullOrWhiteSpace(response.Source) ? "FINANCIAL_LEDGER" : response.Source!,
                AsOfUtc: response.AsOfUtc ?? DateTime.UtcNow,
                IsStale: false,
                IsFallback: false,
                Error: null);
        }

        return BuildFailure(response?.Detail ?? "FinancialLedger effective balance is unavailable.", "TESTNET", "FINANCIAL_LEDGER");
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

    private sealed class ExecutorEffectiveBalanceResponse
    {
        public string? Environment { get; set; }
        public string? Source { get; set; }
        public bool Available { get; set; }
        public decimal? Balance { get; set; }
        public DateTime? AsOfUtc { get; set; }
        public bool Stale { get; set; }
        public string? Detail { get; set; }
    }

    private sealed class LedgerEffectiveBalanceResponse
    {
        public string? Source { get; set; }
        public bool Available { get; set; }
        public decimal? Balance { get; set; }
        public DateTime? AsOfUtc { get; set; }
        public string? Detail { get; set; }
    }

    private sealed record CacheEntry(EffectiveBalanceSnapshot Snapshot, DateTime ValidUntilUtc);
}
