using CryptoTrading.Shared.Session;

namespace Executor.API.Configuration;

public sealed record RecoveryPolicy(
    string DriftType,
    ReconciliationRecoveryMode RecoveryMode,
    bool ShouldAutoCorrect,
    bool RequiresApproval,
    bool AllowsSessionBoundaryCrossing,
    string RecoveryAction);

public static class RecoveryPolicyResolver
{
    /// <summary>
    /// Resolves recovery policy for a detected drift.
    /// </summary>
    /// <param name="settings">Reconciliation settings.</param>
    /// <param name="driftType">POSITION or BALANCE.</param>
    /// <param name="currentSession">Current trading session (may be null).</param>
    /// <param name="signedDriftDelta">
    /// Signed delta: BinanceValue - LocalValue.
    /// Positive  = Binance has more (excess on exchange).
    /// Negative  = local has more (excess in tracker, dangerous for sells).
    /// </param>
    public static RecoveryPolicy Resolve(
        ReconciliationSettings settings,
        string driftType,
        SessionInfo? currentSession,
        decimal signedDriftDelta)
    {
        var normalizedDriftType = driftType.Trim().ToUpperInvariant();
        var nearSessionBoundary = currentSession is not null
            && currentSession.TimeToEnd <= TimeSpan.FromMinutes(Math.Max(1, settings.SessionBoundaryLockMinutes));

        if (normalizedDriftType == "BALANCE")
        {
            return new RecoveryPolicy(
                DriftType: normalizedDriftType,
                RecoveryMode: settings.RecoveryMode,
                ShouldAutoCorrect: false,
                RequiresApproval: settings.BalancePolicy == BalanceDriftPolicy.RequireApproval,
                AllowsSessionBoundaryCrossing: false,
                RecoveryAction: settings.BalancePolicy == BalanceDriftPolicy.RequireApproval
                    ? "BALANCE_PENDING_REVIEW"
                    : "BALANCE_LOGGED");
        }

        // Binance > local (excess on exchange): purely observational.
        // We do NOT auto-correct upward — the local tracker is conservative by design.
        // Logging the observation is sufficient; no position mutation needed.
        if (signedDriftDelta > 0)
        {
            return new RecoveryPolicy(
                DriftType: normalizedDriftType,
                RecoveryMode: settings.RecoveryMode,
                ShouldAutoCorrect: false,
                RequiresApproval: false,
                AllowsSessionBoundaryCrossing: false,
                RecoveryAction: "POSITION_EXCESS_OBSERVED");
        }

        // Binance < local (local tracker has more than Binance): dangerous for sell-side.
        // Apply standard approval/auto-correct policy.
        var requiresApproval = settings.RecoveryMode == ReconciliationRecoveryMode.RequireApproval;
        var shouldAutoCorrect = settings.RecoveryMode == ReconciliationRecoveryMode.AutoCorrect;
        var allowsSessionBoundaryCrossing = settings.AllowCrossSessionCorrection;

        if (nearSessionBoundary && !allowsSessionBoundaryCrossing)
        {
            shouldAutoCorrect = false;
            requiresApproval = true;
        }

        return new RecoveryPolicy(
            DriftType: normalizedDriftType,
            RecoveryMode: settings.RecoveryMode,
            ShouldAutoCorrect: shouldAutoCorrect,
            RequiresApproval: requiresApproval,
            AllowsSessionBoundaryCrossing: allowsSessionBoundaryCrossing,
            RecoveryAction: shouldAutoCorrect
                ? "POSITION_CORRECTED"
                : requiresApproval
                    ? "POSITION_PENDING_REVIEW"
                    : "POSITION_LOGGED");
    }
}
