using CryptoTrading.Shared.DTOs;
using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Microsoft.Extensions.Options;

namespace Executor.API.Services;

/// <summary>
/// Periodically compares Spot exchange truth against local execution/budget state.
/// Delta-only: when no drift is detected, no DB writes and no alerts are emitted.
/// </summary>
public sealed class PeriodicReconciliationService : BackgroundService
{
    private static readonly string[] QuoteSuffixes = ["USDT", "USDC", "BUSD", "FDUSD", "BTC", "ETH", "BNB"];

    private readonly TradingSettings _tradingSettings;
    private readonly BinanceSettings _binanceSettings;
    private readonly BinanceRestClientProvider _clientProvider;
    private readonly PositionTracker _positionTracker;
    private readonly BudgetRepository _budgetRepository;
    private readonly OrderRepository _orderRepository;
    private readonly SystemEventPublisher _systemEvents;
    private readonly ILogger<PeriodicReconciliationService> _logger;
    private readonly SemaphoreSlim _cycleLock = new(1, 1);

    public PeriodicReconciliationService(
        IOptions<TradingSettings> tradingSettings,
        IOptions<BinanceSettings> binanceSettings,
        BinanceRestClientProvider clientProvider,
        PositionTracker positionTracker,
        BudgetRepository budgetRepository,
        OrderRepository orderRepository,
        SystemEventPublisher systemEvents,
        ILogger<PeriodicReconciliationService> logger)
    {
        _tradingSettings = tradingSettings.Value;
        _binanceSettings = binanceSettings.Value;
        _clientProvider = clientProvider;
        _positionTracker = positionTracker;
        _budgetRepository = budgetRepository;
        _orderRepository = orderRepository;
        _systemEvents = systemEvents;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_tradingSettings.Reconciliation.Enabled)
        {
            _logger.LogInformation("Periodic reconciliation is disabled by configuration.");
            return;
        }

        var intervalSeconds = Math.Max(30, _tradingSettings.Reconciliation.IntervalSeconds);
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        _logger.LogInformation(
            "Periodic reconciliation started. Interval={IntervalSeconds}s, Testnet={UseTestnet}",
            intervalSeconds,
            _binanceSettings.UseTestnet);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunCycleSafeAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            timer.Dispose();
        }
    }

    private async Task RunCycleSafeAsync(CancellationToken ct)
    {
        if (!await _cycleLock.WaitAsync(0, ct))
        {
            _logger.LogWarning("Periodic reconciliation skipped because previous cycle is still running.");
            return;
        }

        try
        {
            await RunCycleAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Periodic reconciliation cycle failed.");
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var reconciliationId = Guid.NewGuid();
        var reconciliationUtc = DateTime.UtcNow;
        var drifts = new List<OrderRepository.StateDriftLogInput>();

        var accountInfoResult = await _clientProvider.Current.SpotApi.Account.GetAccountInfoAsync(ct: ct);
        if (!accountInfoResult.Success || accountInfoResult.Data is null)
        {
            _logger.LogWarning("Reconciliation {ReconciliationId}: failed to fetch Spot account info: {Error}",
                reconciliationId,
                accountInfoResult.Error?.Message);
            return;
        }

        var balancesByAsset = BuildBalancesByAsset(accountInfoResult.Data.Balances);

        // Position drift: compare local open position quantity vs Binance spot asset total.
        var positionTolerance = _tradingSettings.Reconciliation.PositionQuantityTolerance;
        foreach (var pos in _positionTracker.GetRawPositions())
        {
            var baseAsset = TryExtractBaseAsset(pos.Symbol);
            if (baseAsset is null)
                continue;

            var binanceQty = balancesByAsset.GetValueOrDefault(baseAsset, 0m);
            if (Math.Abs(binanceQty - pos.Quantity) <= positionTolerance)
                continue;

            drifts.Add(new OrderRepository.StateDriftLogInput(
                DriftType: "POSITION",
                Symbol: pos.Symbol,
                BinanceValue: binanceQty,
                LocalValue: pos.Quantity,
                Severity: "WARNING",
                RecoveryAction: "NONE",
                RecoveryDetail: "Drift detected by periodic spot reconciliation.",
                RecoveryAttempted: false,
                RecoverySuccess: false));
        }

        // Mainnet only: informational balance drift against virtual cash balance.
        if (!_binanceSettings.UseTestnet)
        {
            var quoteAsset = string.IsNullOrWhiteSpace(_tradingSettings.Reconciliation.QuoteAsset)
                ? "USDT"
                : _tradingSettings.Reconciliation.QuoteAsset.Trim().ToUpperInvariant();

            var binanceBalance = balancesByAsset.GetValueOrDefault(quoteAsset, 0m);
            var localBudget = await _budgetRepository.GetBudgetStatusAsync(ct);
            var localBalance = localBudget?.CurrentCashBalance ?? 0m;
            var balanceTolerance = _tradingSettings.Reconciliation.BalanceDriftTolerance;

            if (Math.Abs(binanceBalance - localBalance) > balanceTolerance)
            {
                drifts.Add(new OrderRepository.StateDriftLogInput(
                    DriftType: "BALANCE",
                    Symbol: null,
                    BinanceValue: binanceBalance,
                    LocalValue: localBalance,
                    Severity: "INFO",
                    RecoveryAction: "NONE",
                    RecoveryDetail: "Virtual budget drift observed vs exchange quote asset balance.",
                    RecoveryAttempted: false,
                    RecoverySuccess: false));
            }
        }

        if (drifts.Count == 0)
        {
            _logger.LogDebug("Reconciliation {ReconciliationId}: no drift detected.", reconciliationId);
            return;
        }

        var environment = _binanceSettings.UseTestnet ? "TESTNET" : "LIVE";
        await _orderRepository.InsertStateDriftLogsAsync(reconciliationId, reconciliationUtc, environment, drifts, ct);

        var symbolCount = drifts.Count(d => !string.IsNullOrWhiteSpace(d.Symbol));
        var preview = string.Join(", ",
            drifts.Where(d => !string.IsNullOrWhiteSpace(d.Symbol))
                .Select(d => d.Symbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, _tradingSettings.Reconciliation.AlertMaxSymbols)));

        var summary =
            $"ReconciliationId={reconciliationId}, drifts={drifts.Count}, symbols={symbolCount}, env={environment}" +
            (string.IsNullOrEmpty(preview) ? string.Empty : $", sample=[{preview}]");

        await _systemEvents.PublishAsync(new SystemEvent
        {
            Type = SystemEventType.ReconciliationDrift,
            ServiceName = "Executor",
            Message = summary,
            Timestamp = DateTime.UtcNow,
            ErrorCode = null,
            Symbol = null
        }, ct);

        _logger.LogWarning("{Summary}", summary);
    }

    private static Dictionary<string, decimal> BuildBalancesByAsset(IEnumerable<object> balances)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var balance in balances)
        {
            var asset = GetStringProperty(balance, "Asset");
            if (string.IsNullOrWhiteSpace(asset))
                continue;

            var total = GetDecimalProperty(balance, "Total");
            if (total <= 0)
            {
                // Binance.Net model differences between versions.
                var available = GetDecimalProperty(balance, "Available");
                var free = GetDecimalProperty(balance, "Free");
                var locked = GetDecimalProperty(balance, "Locked");
                total = Math.Max(0m, available > 0 ? available + locked : free + locked);
            }

            result[asset.ToUpperInvariant()] = total;
        }

        return result;
    }

    private static string? TryExtractBaseAsset(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var normalized = symbol.Trim().ToUpperInvariant();
        foreach (var quote in QuoteSuffixes)
        {
            if (normalized.EndsWith(quote, StringComparison.Ordinal) && normalized.Length > quote.Length)
                return normalized[..^quote.Length];
        }

        return null;
    }

    private static string? GetStringProperty(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        var value = prop?.GetValue(obj);
        return value?.ToString();
    }

    private static decimal GetDecimalProperty(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        var raw = prop?.GetValue(obj);
        if (raw is null)
            return 0m;

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
