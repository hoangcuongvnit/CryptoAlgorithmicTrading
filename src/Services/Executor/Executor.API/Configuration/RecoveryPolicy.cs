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
    public static RecoveryPolicy Resolve(
        ReconciliationSettings settings,
        string driftType,
        SessionInfo? currentSession,
        decimal driftAmount)
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
