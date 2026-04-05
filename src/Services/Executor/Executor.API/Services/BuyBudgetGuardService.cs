using CryptoTrading.Shared.DTOs;
using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Executor.API.Services;

public sealed record BuyBudgetGuardResult(
    bool Passed,
    string ErrorMessage,
    TradingErrorCode ErrorCode,
    decimal RequiredCash,
    decimal AvailableCash,
    string Source,
    DateTime? SnapshotUpdatedAtUtc);

public sealed class BuyBudgetGuardService
{
    private const decimal TestnetFallbackBalance = 100m;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CashBalanceStateService _cashBalanceState;
    private readonly BinanceSettings _binanceSettings;
    private readonly ReconciliationSettings _reconciliationSettings;
    private readonly ILogger<BuyBudgetGuardService> _logger;

    public BuyBudgetGuardService(
        IHttpClientFactory httpClientFactory,
        CashBalanceStateService cashBalanceState,
        IOptions<BinanceSettings> binanceSettings,
        IOptions<TradingSettings> tradingSettings,
        ILogger<BuyBudgetGuardService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cashBalanceState = cashBalanceState;
        _binanceSettings = binanceSettings.Value;
        _reconciliationSettings = tradingSettings.Value.Reconciliation;
        _logger = logger;
    }

    public async Task<BuyBudgetGuardResult> ValidateAsync(OrderRequest orderRequest, decimal effectivePrice, CancellationToken ct)
    {
        if (orderRequest.Side != OrderSide.Buy)
        {
            return new BuyBudgetGuardResult(true, string.Empty, TradingErrorCode.None, 0m, 0m, string.Empty, null);
        }

        if (effectivePrice <= 0m)
        {
            return new BuyBudgetGuardResult(false, $"Unable to resolve a reference price for {orderRequest.Symbol}.", TradingErrorCode.NoReferencePrice, 0m, 0m, string.Empty, null);
        }

        var requiredCash = decimal.Round(orderRequest.Quantity * effectivePrice, 8);
        if (requiredCash <= 0m)
        {
            return new BuyBudgetGuardResult(false, $"Unable to calculate required cash for {orderRequest.Symbol}.", TradingErrorCode.InvalidOrderParameters, requiredCash, 0m, string.Empty, null);
        }

        if (_binanceSettings.UseTestnet)
        {
            var availableCash = TestnetFallbackBalance;
            var source = "FINANCIAL_LEDGER_FALLBACK";
            DateTime? snapshotUpdatedAt = DateTime.UtcNow;

            try
            {
                var client = _httpClientFactory.CreateClient("financialledger");
                var response = await client.GetAsync("/api/ledger/balance/effective?environment=TESTNET&baseCurrency=USDT", ct);
                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadFromJsonAsync<FinancialLedgerEffectiveBalanceResponse>(cancellationToken: ct);
                    if (payload is not null && payload.Available && payload.Balance.HasValue)
                    {
                        availableCash = Math.Max(0m, payload.Balance.Value);
                        source = "FINANCIAL_LEDGER";
                        snapshotUpdatedAt = payload.AsOfUtc ?? DateTime.UtcNow;
                    }
                }
                else
                {
                    _logger.LogWarning("FinancialLedger effective balance lookup returned HTTP {StatusCode}. Applying fallback {FallbackBalance:F2}.", (int)response.StatusCode, TestnetFallbackBalance);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch TESTNET effective balance from FinancialLedger. Applying fallback {FallbackBalance:F2}.", TestnetFallbackBalance);
            }

            if (availableCash + 0.00000001m < requiredCash)
            {
                var errorMessage = $"Buy blocked: required cash {requiredCash:F8} exceeds testnet cash {availableCash:F8} (source={source}).";
                _logger.LogWarning(errorMessage);
                return new BuyBudgetGuardResult(false, errorMessage, TradingErrorCode.InsufficientCashBalance, requiredCash, availableCash, source, snapshotUpdatedAt);
            }

            return new BuyBudgetGuardResult(true, string.Empty, TradingErrorCode.None, requiredCash, availableCash, source, snapshotUpdatedAt);
        }

        if (!_cashBalanceState.TryGetMainnetSnapshot(out var snapshot))
        {
            return new BuyBudgetGuardResult(false, "Mainnet cash snapshot is unavailable. Wait for reconciliation to sync cash balance.", TradingErrorCode.CashBalanceSnapshotUnavailable, requiredCash, 0m, "MAINNET_RECONCILED", null);
        }

        var snapshotAge = DateTime.UtcNow - snapshot.UpdatedAtUtc;
        var maxAge = TimeSpan.FromMinutes(Math.Max(1, _reconciliationSettings.CashSnapshotMaxAgeMinutes));
        if (snapshotAge > maxAge)
        {
            var errorMessage = $"Mainnet cash snapshot is stale ({snapshotAge.TotalMinutes:F1}m old). Wait for reconciliation to refresh the synced balance.";
            _logger.LogWarning(errorMessage);
            return new BuyBudgetGuardResult(false, errorMessage, TradingErrorCode.CashBalanceSnapshotUnavailable, requiredCash, snapshot.CashBalance, snapshot.Source, snapshot.UpdatedAtUtc);
        }

        var mainnetCash = snapshot.CashBalance;
        if (mainnetCash + 0.00000001m < requiredCash)
        {
            var errorMessage = $"Buy blocked: required cash {requiredCash:F8} exceeds reconciled mainnet cash {mainnetCash:F8}.";
            _logger.LogWarning(errorMessage);
            return new BuyBudgetGuardResult(false, errorMessage, TradingErrorCode.InsufficientCashBalance, requiredCash, mainnetCash, snapshot.Source, snapshot.UpdatedAtUtc);
        }

        return new BuyBudgetGuardResult(true, string.Empty, TradingErrorCode.None, requiredCash, mainnetCash, snapshot.Source, snapshot.UpdatedAtUtc);
    }

    private sealed class FinancialLedgerEffectiveBalanceResponse
    {
        public bool Available { get; set; }
        public decimal? Balance { get; set; }
        public DateTime? AsOfUtc { get; set; }
    }
}