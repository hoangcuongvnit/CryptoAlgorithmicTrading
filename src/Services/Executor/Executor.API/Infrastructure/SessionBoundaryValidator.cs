using CryptoTrading.Shared.Session;

namespace Executor.API.Infrastructure;

public sealed class SessionBoundaryValidator
{
    public bool CanApplyCorrection(
        SessionInfo? currentSession,
        bool allowCrossSessionCorrection,
        int sessionBoundaryLockMinutes)
    {
        if (allowCrossSessionCorrection || currentSession is null)
            return true;

        var lockWindow = TimeSpan.FromMinutes(Math.Max(1, sessionBoundaryLockMinutes));
        return currentSession.TimeToEnd > lockWindow;
    }
}
