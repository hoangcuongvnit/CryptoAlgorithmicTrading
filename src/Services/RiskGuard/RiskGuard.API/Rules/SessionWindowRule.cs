using CryptoTrading.Shared.Session;

namespace RiskGuard.API.Rules;

public sealed class SessionWindowRule : IRiskRule
{
    private readonly SessionClock _sessionClock;
    private readonly SessionTradingPolicy _policy;
    private readonly SessionSettings _settings;

    public SessionWindowRule(
        SessionClock sessionClock,
        SessionTradingPolicy policy,
        Microsoft.Extensions.Options.IOptions<SessionSettings> settings)
    {
        _sessionClock = sessionClock;
        _policy = policy;
        _settings = settings.Value;
    }

    public string Name => "SessionWindow";

    public ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        if (!_settings.Enabled)
            return ValueTask.FromResult(RuleResult.Pass());

        if (context.IsReduceOnly)
            return ValueTask.FromResult(RuleResult.Pass());

        var session = _sessionClock.GetCurrentSession();

        if (!_policy.CanOpenNewPosition(session))
        {
            return ValueTask.FromResult(RuleResult.Reject(
                $"New positions blocked: session {session.SessionId} is in {session.CurrentPhase} phase. " +
                $"Time to session end: {session.TimeToEnd.TotalMinutes:F0}m"));
        }

        return ValueTask.FromResult(RuleResult.Pass());
    }
}
