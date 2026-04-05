using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Session;
using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Microsoft.Extensions.Options;
using System.Diagnostics;

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
    private readonly CashBalanceStateService _cashBalanceState;
    private readonly BudgetRepository _budgetRepository;
    private readonly OrderRepository _orderRepository;
    private readonly SessionClock _sessionClock;
    private readonly SessionSettings _sessionSettings;
    private readonly SessionTradingPolicy _sessionPolicy;
    private readonly SessionBoundaryValidator _sessionBoundaryValidator;
    private readonly ReconciliationMetrics _metrics;
    private readonly SystemEventPublisher _systemEvents;
    private readonly ILogger<PeriodicReconciliationService> _logger;
    private readonly SemaphoreSlim _cycleLock = new(1, 1);

    public PeriodicReconciliationService(
        IOptions<TradingSettings> tradingSettings,
        IOptions<BinanceSettings> binanceSettings,
        BinanceRestClientProvider clientProvider,
        PositionTracker positionTracker,
        CashBalanceStateService cashBalanceState,
        BudgetRepository budgetRepository,
        OrderRepository orderRepository,
        SessionClock sessionClock,
        IOptions<SessionSettings> sessionSettings,
        SessionTradingPolicy sessionPolicy,
        SessionBoundaryValidator sessionBoundaryValidator,
        ReconciliationMetrics metrics,
        SystemEventPublisher systemEvents,
        ILogger<PeriodicReconciliationService> logger)
    {
        _tradingSettings = tradingSettings.Value;
        _binanceSettings = binanceSettings.Value;
        _clientProvider = clientProvider;
        _positionTracker = positionTracker;
        _cashBalanceState = cashBalanceState;
        _budgetRepository = budgetRepository;
        _orderRepository = orderRepository;
        _sessionClock = sessionClock;
        _sessionSettings = sessionSettings.Value;
        _sessionPolicy = sessionPolicy;
        _sessionBoundaryValidator = sessionBoundaryValidator;
        _metrics = metrics;
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
        var stopwatch = Stopwatch.StartNew();
        _metrics.RecordCycleStart();

        if (!await _cycleLock.WaitAsync(0, ct))
        {
            _logger.LogWarning("Periodic reconciliation skipped because previous cycle is still running.");
            stopwatch.Stop();
            _metrics.RecordCycleComplete(stopwatch.Elapsed, success: false);
            return;
        }

        var success = false;
        try
        {
            var result = await RunCycleAsync(ct);
            _metrics.RecordDrifts(result.Detected);
            _metrics.RecordRecovered(result.Recovered);
            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Periodic reconciliation cycle failed.");
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordCycleComplete(stopwatch.Elapsed, success);
            _cycleLock.Release();
        }
    }

    private async Task<(int Detected, int Recovered)> RunCycleAsync(CancellationToken ct)
    {
        var reconciliationId = Guid.NewGuid();
        var reconciliationUtc = DateTime.UtcNow;
        var candidates = new List<DriftCandidate>();

        var currentSession = _sessionSettings.Enabled
            ? _sessionClock.GetCurrentSession()
            : null;

        var accountInfoResult = await _clientProvider.Current.SpotApi.Account.GetAccountInfoAsync(ct: ct);
        if (!accountInfoResult.Success || accountInfoResult.Data is null)
        {
            _logger.LogWarning("Reconciliation {ReconciliationId}: failed to fetch Spot account info: {Error}",
                reconciliationId,
                accountInfoResult.Error?.Message);
            return (0, 0);
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

            candidates.Add(new DriftCandidate(
                DriftType: "POSITION",
                Symbol: pos.Symbol,
                BinanceValue: binanceQty,
                LocalValue: pos.Quantity,
                Severity: "WARNING"));
        }

        // Mainnet only: informational balance drift against virtual cash balance.
        if (!_binanceSettings.UseTestnet)
        {
            var quoteAsset = string.IsNullOrWhiteSpace(_tradingSettings.Reconciliation.QuoteAsset)
                ? "USDT"
                : _tradingSettings.Reconciliation.QuoteAsset.Trim().ToUpperInvariant();

            var binanceBalance = balancesByAsset.GetValueOrDefault(quoteAsset, 0m);
            _cashBalanceState.UpdateMainnetSnapshot(binanceBalance, reconciliationUtc, "reconciliation");
            var localBudget = await _budgetRepository.GetBudgetStatusAsync(ct);
            var localBalance = localBudget?.CurrentCashBalance ?? 0m;
            var balanceTolerance = _tradingSettings.Reconciliation.BalanceDriftTolerance;

            if (Math.Abs(binanceBalance - localBalance) > balanceTolerance)
            {
                candidates.Add(new DriftCandidate(
                    DriftType: "BALANCE",
                    Symbol: null,
                    BinanceValue: binanceBalance,
                    LocalValue: localBalance,
                    Severity: "INFO"));
            }
        }

        if (candidates.Count == 0)
        {
            _logger.LogDebug("Reconciliation {ReconciliationId}: no drift detected.", reconciliationId);
            return (0, 0);
        }

        var lockdown = candidates.Count > _tradingSettings.Reconciliation.MaxDriftsPerCycleLockdown;
        var environment = _binanceSettings.UseTestnet ? "TESTNET" : "LIVE";
        var drifts = new List<OrderRepository.StateDriftLogInput>(candidates.Count);
        var recoveredCount = 0;
        var pendingReviewCount = 0;

        foreach (var candidate in candidates)
        {
            var policy = RecoveryPolicyResolver.Resolve(_tradingSettings.Reconciliation, candidate.DriftType, currentSession, Math.Abs(candidate.BinanceValue - candidate.LocalValue));
            var recoveryAttempted = false;
            var recoverySuccess = false;
            var recoveryAction = policy.RecoveryAction;
            var recoveryDetail = candidate.DriftType == "POSITION"
                ? $"Local qty {candidate.LocalValue:F8} → Binance qty {candidate.BinanceValue:F8}."
                : $"Local balance {candidate.LocalValue:F2} → Binance balance {candidate.BinanceValue:F2}.";

            if (candidate.DriftType == "POSITION" && !lockdown && policy.ShouldAutoCorrect)
            {
                if (currentSession is not null && _sessionPolicy.IsReduceOnlyWindow(currentSession))
                {
                    recoveryAction = "POSITION_DEFERRED_REDUCE_ONLY_WINDOW";
                    recoveryDetail = "Drift detected but correction deferred because the session is in a reduce-only window.";
                    pendingReviewCount++;
                }
                else if (_sessionBoundaryValidator.CanApplyCorrection(
                        currentSession,
                        policy.AllowsSessionBoundaryCrossing,
                        _tradingSettings.Reconciliation.SessionBoundaryLockMinutes))
                {
                    _positionTracker.CorrectPosition(candidate.Symbol!, candidate.BinanceValue);
                    recoveryAttempted = true;
                    recoverySuccess = true;
                    recoveredCount++;
                    recoveryAction = candidate.BinanceValue <= 0m ? "POSITION_REMOVED" : "POSITION_CORRECTED";
                    recoveryDetail = $"Auto-corrected by periodic spot reconciliation. Local qty {candidate.LocalValue:F8} → Binance qty {candidate.BinanceValue:F8}.";
                }
                else
                {
                    recoveryAction = "POSITION_DEFERRED_SESSION_BOUNDARY";
                    recoveryDetail = "Drift detected but correction deferred because the session boundary lock is active.";
                    pendingReviewCount++;
                }
            }
            else if (candidate.DriftType == "POSITION" && lockdown)
            {
                recoveryAction = "POSITION_LOCKDOWN";
                recoveryDetail = "Drift detected but auto-correction was blocked because the cycle exceeded the drift lockdown threshold.";
                pendingReviewCount++;
            }
            else if (candidate.DriftType == "BALANCE")
            {
                recoveryAction = _tradingSettings.Reconciliation.BalancePolicy == BalanceDriftPolicy.RequireApproval
                    ? "BALANCE_PENDING_REVIEW"
                    : "BALANCE_LOGGED";
                pendingReviewCount++;
            }

            drifts.Add(new OrderRepository.StateDriftLogInput(
                DriftType: candidate.DriftType,
                Symbol: candidate.Symbol,
                BinanceValue: candidate.BinanceValue,
                LocalValue: candidate.LocalValue,
                Severity: candidate.Severity,
                RecoveryAction: recoveryAction,
                RecoveryDetail: recoveryDetail,
                RecoveryAttempted: recoveryAttempted,
                RecoverySuccess: recoverySuccess));
        }

        await _orderRepository.InsertStateDriftLogsAsync(reconciliationId, reconciliationUtc, environment, drifts, ct);

        // Build rich alert message — constructed inside drift branch (memory optimization per spec §6.3)
        var positionDrifts = drifts.Where(d => d.DriftType == "POSITION").ToList();
        var balanceDrift   = drifts.FirstOrDefault(d => d.DriftType == "BALANCE");
        var shortId        = reconciliationId.ToString("N")[..8];

        var sb = new System.Text.StringBuilder();

        sb.Append($"🔎 STATE DRIFT [{environment}]\n");
        sb.Append($"• Detected: {drifts.Count} | Recovered: {recoveredCount} | Pending review: {pendingReviewCount}\n");
        if (lockdown)
            sb.Append($"• Lockdown: triggered because drift count exceeded {_tradingSettings.Reconciliation.MaxDriftsPerCycleLockdown}\n");

        if (positionDrifts.Count > 0)
            sb.Append($"• Position drifts: {positionDrifts.Count}\n");

        foreach (var d in positionDrifts)
        {
            sb.Append($"  - {d.Symbol}: {d.LocalValue:F8} → {d.BinanceValue:F8} [{d.RecoveryAction}]\n");
        }

        if (balanceDrift is not null)
        {
            var quoteLabel = string.IsNullOrWhiteSpace(_tradingSettings.Reconciliation.QuoteAsset)
                ? "USDT"
                : _tradingSettings.Reconciliation.QuoteAsset.Trim().ToUpperInvariant();
            sb.Append($"• Balance drift: Local={balanceDrift.LocalValue:F2} → Binance={balanceDrift.BinanceValue:F2} {quoteLabel} [{balanceDrift.RecoveryAction}]\n");
        }

        sb.Append($"ID: {shortId}");

        var alertMessage = sb.ToString();

        await _systemEvents.PublishAsync(new SystemEvent
        {
            Type        = SystemEventType.ReconciliationDrift,
            ServiceName = "Executor",
            Message     = alertMessage,
            Timestamp   = DateTime.UtcNow,
            ErrorCode   = null,
            Symbol      = null
        }, ct);

        _logger.LogWarning("{AlertMessage}", alertMessage);
        return (drifts.Count, recoveredCount);
    }

    private sealed record DriftCandidate(
        string DriftType,
        string? Symbol,
        decimal BinanceValue,
        decimal LocalValue,
        string Severity);

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
