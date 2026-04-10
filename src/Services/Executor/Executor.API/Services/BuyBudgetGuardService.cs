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
    private readonly BinanceSettings _binanceSettings;
    private readonly ILogger<BuyBudgetGuardService> _logger;

    public BuyBudgetGuardService(
        IHttpClientFactory httpClientFactory,
        IOptions<BinanceSettings> binanceSettings,
        ILogger<BuyBudgetGuardService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _binanceSettings = binanceSettings.Value;
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

        var availableCash = TestnetFallbackBalance;
        var source = "FINANCIAL_LEDGER_FALLBACK";
        DateTime? snapshotUpdatedAt = DateTime.UtcNow;

        try
        {
            var client = _httpClientFactory.CreateClient("financialledger");
            var environment = _binanceSettings.UseTestnet ? "TESTNET" : "MAINNET";
            var response = await client.GetAsync($"/api/ledger/balance/effective?environment={environment}&baseCurrency=USDT", ct);
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
            _logger.LogWarning(ex, "Failed to fetch effective balance from FinancialLedger. Applying fallback {FallbackBalance:F2}.", TestnetFallbackBalance);
        }

        if (availableCash + 0.00000001m < requiredCash)
        {
            var errorMessage = $"Buy blocked: required cash {requiredCash:F8} exceeds available cash {availableCash:F8} (source={source}).";
            _logger.LogWarning(errorMessage);
            return new BuyBudgetGuardResult(false, errorMessage, TradingErrorCode.InsufficientCashBalance, requiredCash, availableCash, source, snapshotUpdatedAt);
        }

        return new BuyBudgetGuardResult(true, string.Empty, TradingErrorCode.None, requiredCash, availableCash, source, snapshotUpdatedAt);
    }

    private sealed class FinancialLedgerEffectiveBalanceResponse
    {
        public bool Available { get; set; }
        public decimal? Balance { get; set; }
        public DateTime? AsOfUtc { get; set; }
    }
}