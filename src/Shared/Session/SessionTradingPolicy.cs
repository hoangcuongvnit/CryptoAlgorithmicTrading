using CryptoTrading.Shared.DTOs;

namespace CryptoTrading.Shared.Session;

public sealed class SessionTradingPolicy
{
    public bool CanOpenNewPosition(SessionInfo session)
        => session.CurrentPhase == SessionPhase.Open;

    public bool MustForceClose(SessionInfo session)
        => session.CurrentPhase == SessionPhase.ForcedFlatten;

    public bool IsReduceOnlyWindow(SessionInfo session)
        => session.CurrentPhase is SessionPhase.LiquidationOnly or SessionPhase.ForcedFlatten;

    public bool IsCrossSessionOrder(string? orderSessionId, SessionInfo currentSession)
        => !string.IsNullOrEmpty(orderSessionId) && orderSessionId != currentSession.SessionId;
}
