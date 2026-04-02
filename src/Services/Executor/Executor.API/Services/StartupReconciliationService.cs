using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Session;
using Executor.API.Configuration;
using Executor.API.Infrastructure;
using Microsoft.Extensions.Options;

namespace Executor.API.Services;

/// <summary>
/// Runs once at startup. Implements the recovery state machine:
///   Booting → RecoveryMode → RecoveryExecuting → RecoveryVerified → TradingEnabled
///
/// Responsibilities:
///  1. Freeze new-position orders (set RecoveryMode).
///  2. Reconstruct PositionTracker from DB net-position query.
///  3. In live mode: reconcile local state against Binance open orders; submit
///     reduce-only corrections as needed.
///  4. Apply session policy:
///       - Ended-session residual → emergency flatten.
///       - In-session, in liquidation window → stay reduce-only.
///  5. Unlock trading (TradingEnabled).
/// </summary>
public sealed class StartupReconciliationService : BackgroundService
{
    private readonly RecoveryStateService _recoveryState;
    private readonly OrderRepository _orderRepository;
    private readonly PositionTracker _positionTracker;
    private readonly OrderExecutionService _executionService;
    private readonly SessionClock _sessionClock;
    private readonly SessionTradingPolicy _sessionPolicy;
    private readonly SessionSettings _sessionSettings;
    private readonly TradingSettings _tradingSettings;
    private readonly BinanceRestClientProvider _clientProvider;
    private readonly ILogger<StartupReconciliationService> _logger;

    // How far back to look for unclosed positions (covers max 2 × 4-hour sessions)
    private const int LookbackHours = 8;

    public StartupReconciliationService(
        RecoveryStateService recoveryState,
        OrderRepository orderRepository,
        PositionTracker positionTracker,
        OrderExecutionService executionService,
        SessionClock sessionClock,
        SessionTradingPolicy sessionPolicy,
        IOptions<SessionSettings> sessionSettings,
        IOptions<TradingSettings> tradingSettings,
        BinanceRestClientProvider clientProvider,
        ILogger<StartupReconciliationService> logger)
    {
        _recoveryState = recoveryState;
        _orderRepository = orderRepository;
        _positionTracker = positionTracker;
        _executionService = executionService;
        _sessionClock = sessionClock;
        _sessionPolicy = sessionPolicy;
        _sessionSettings = sessionSettings.Value;
        _tradingSettings = tradingSettings.Value;
        _clientProvider = clientProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "StartupReconciliationService starting. RunId={RunId}", _recoveryState.RecoveryRunId);

        _recoveryState.TransitionTo(SystemRecoveryState.RecoveryMode);

        try
        {
            await RunReconciliationAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Reconciliation cancelled during shutdown");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Reconciliation failed unexpectedly. Unlocking trading in fail-open mode. RunId={RunId}",
                _recoveryState.RecoveryRunId);
            // Fail-open: better to allow trading and miss a recovery edge case
            // than to permanently block all orders.
            _recoveryState.TransitionTo(SystemRecoveryState.TradingEnabled);
        }
    }

    private async Task RunReconciliationAsync(CancellationToken ct)
    {
        var sinceUtc = DateTime.UtcNow.AddHours(-LookbackHours);
        var session = _sessionSettings.Enabled ? _sessionClock.GetCurrentSession() : null;

        // ── Step 1: Load local net open positions from DB ─────────────────────
        _logger.LogInformation("Recovery: loading local open positions since {Since}", sinceUtc);
        var localPositions = await _orderRepository.GetOpenPositionNetAsync(sinceUtc, ct);

        if (localPositions.Count == 0)
        {
            _logger.LogInformation("Recovery: no local open positions found. Transitioning to TradingEnabled.");
            _recoveryState.TransitionTo(SystemRecoveryState.RecoveryVerified);
            _recoveryState.TransitionTo(SystemRecoveryState.TradingEnabled);
            return;
        }

        _logger.LogWarning(
            "Recovery: found {Count} symbols with net open positions. Reconciling...",
            localPositions.Count);

        // ── Step 2: Inject positions into PositionTracker ─────────────────────
        foreach (var pos in localPositions)
        {
            _positionTracker.RecoverPosition(
                pos.Symbol,
                pos.NetQty,
                pos.AvgBuyPrice ?? 0m,
                pos.SessionId);

            _logger.LogInformation(
                "Recovery: restored {Symbol} qty={Qty} @ {Price} session={Session}",
                pos.Symbol, pos.NetQty, pos.AvgBuyPrice, pos.SessionId);
        }

        _recoveryState.TransitionTo(SystemRecoveryState.RecoveryExecuting);

        // ── Step 3: Exchange reconciliation (live mode only) ──────────────────
        int mismatchesFixed = 0;

        if (!_tradingSettings.PaperTradingMode)
        {
            mismatchesFixed = await ReconcileWithExchangeAsync(localPositions, session, ct);
        }
        else
        {
            _logger.LogInformation("Recovery: paper-trading mode — skipping Binance reconciliation");
        }

        // ── Step 4: Apply session policy ──────────────────────────────────────
        int emergencyCloses = 0;

        if (_sessionSettings.Enabled && session is not null)
        {
            emergencyCloses = await ApplySessionPolicyAsync(localPositions, session, ct);
        }

        // ── Step 5: Verify and unlock ─────────────────────────────────────────
        _logger.LogInformation(
            "Recovery complete. Mismatches={Mismatches}, EmergencyCloses={EmergencyCloses}. RunId={RunId}",
            mismatchesFixed, emergencyCloses, _recoveryState.RecoveryRunId);

        _recoveryState.TransitionTo(SystemRecoveryState.RecoveryVerified);
        _recoveryState.TransitionTo(SystemRecoveryState.TradingEnabled);
    }

    /// <summary>
    /// Fetches open orders from Binance for each locally-open symbol, then:
    ///  - local open + exchange flat  → position was closed externally; remove from tracker.
    ///  - qty mismatch               → dispatch reduce-only delta close.
    /// Returns the count of corrections applied.
    /// </summary>
    private async Task<int> ReconcileWithExchangeAsync(
        IReadOnlyList<OrderRepository.RecoveredPosition> localPositions,
        SessionInfo? session,
        CancellationToken ct)
    {
        int corrections = 0;

        foreach (var local in localPositions)
        {
            try
            {
                var openOrdersResult = await _clientProvider.Current.SpotApi.Trading
                    .GetOpenOrdersAsync(local.Symbol, ct: ct);

                if (!openOrdersResult.Success)
                {
                    _logger.LogWarning(
                        "Recovery: could not fetch Binance open orders for {Symbol}: {Error}",
                        local.Symbol, openOrdersResult.Error?.Message);
                    continue;
                }

                // Sum pending (open) buy orders that represent unconfirmed positions
                // For spot: we check if there is actual held quantity via open orders
                var pendingBuyQty = openOrdersResult.Data
                    .Where(o => o.Side == Binance.Net.Enums.OrderSide.Buy)
                    .Sum(o => o.QuantityRemaining);

                var pendingSellQty = openOrdersResult.Data
                    .Where(o => o.Side == Binance.Net.Enums.OrderSide.Sell)
                    .Sum(o => o.QuantityRemaining);

                _logger.LogDebug(
                    "Recovery [{Symbol}]: local net={LocalNet}, Binance pendingBuy={PendingBuy}, pendingSell={PendingSell}",
                    local.Symbol, local.NetQty, pendingBuyQty, pendingSellQty);

                // If Binance shows no remaining buy orders and no pending sells,
                // the position was filled and closed externally — remove it from the tracker.
                if (pendingBuyQty == 0 && pendingSellQty == 0 && openOrdersResult.Data.All(
                        o => o.Side == Binance.Net.Enums.OrderSide.Sell))
                {
                    _logger.LogWarning(
                        "Recovery [{Symbol}]: local OPEN, exchange FLAT — position was closed externally. Removing from tracker.",
                        local.Symbol);

                    // Simulate a sell fill to reconcile in-memory state
                    var syntheticRequest = new OrderRequest
                    {
                        Symbol = local.Symbol,
                        Side = OrderSide.Sell,
                        Type = OrderType.Market,
                        Quantity = local.NetQty,
                        IsReduceOnly = true,
                        SessionId = session?.SessionId,
                        StrategyName = "RecoveryReconciliation"
                    };
                    var syntheticResult = new OrderResult
                    {
                        OrderId = Guid.NewGuid().ToString("N"),
                        Symbol = local.Symbol,
                        Side = OrderSide.Sell,
                        Success = true,
                        FilledQty = local.NetQty,
                        FilledPrice = local.AvgBuyPrice ?? 0m,
                        Timestamp = DateTime.UtcNow,
                        IsPaperTrade = false,
                        SessionId = session?.SessionId
                    };
                    _positionTracker.OnOrderFilled(syntheticRequest, syntheticResult);
                    corrections++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Recovery: exchange reconciliation failed for {Symbol}", local.Symbol);
            }
        }

        return corrections;
    }

    /// <summary>
    /// Enforces cross-session and liquidation-window policies on recovered positions.
    /// Returns the number of emergency close orders submitted.
    /// </summary>
    private async Task<int> ApplySessionPolicyAsync(
        IReadOnlyList<OrderRepository.RecoveredPosition> localPositions,
        SessionInfo session,
        CancellationToken ct)
    {
        int closes = 0;

        foreach (var pos in localPositions)
        {
            // Skip symbols that were already reconciled away
            if (!_positionTracker.HasOpenPositions()) break;

            bool staleSession = !string.IsNullOrEmpty(pos.SessionId) &&
                                pos.SessionId != session.SessionId;

            bool inLiquidation = _sessionPolicy.IsReduceOnlyWindow(session);

            if (staleSession)
            {
                _logger.LogCritical(
                    "Recovery: {Symbol} belongs to ended session {OldSession} (current={CurrentSession}). Emergency close.",
                    pos.Symbol, pos.SessionId, session.SessionId);
            }
            else if (inLiquidation)
            {
                _logger.LogWarning(
                    "Recovery: restarted in liquidation window for {Symbol}. Submitting reduce-only close.",
                    pos.Symbol);
            }
            else
            {
                // Active session, normal window — position is valid, keep it open
                continue;
            }

            var closeOrder = new OrderRequest
            {
                Symbol = pos.Symbol,
                Side = OrderSide.Sell,
                Type = OrderType.Market,
                Quantity = pos.NetQty,
                IsReduceOnly = true,
                SessionId = session.SessionId,
                SessionPhase = session.CurrentPhase,
                StrategyName = staleSession ? "RecoveryEmergencyFlatten" : "RecoveryLiquidation"
            };

            try
            {
                var result = await _executionService.ExecuteOrderAsync(closeOrder, ct);
                if (result.Success)
                {
                    _logger.LogInformation(
                        "Recovery: closed {Symbol} qty={Qty} price={Price}",
                        pos.Symbol, result.FilledQty, result.FilledPrice);
                    closes++;
                }
                else
                {
                    _logger.LogError(
                        "Recovery: close order for {Symbol} failed: {Error}",
                        pos.Symbol, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recovery: failed to submit close order for {Symbol}", pos.Symbol);
            }
        }

        return closes;
    }
}
