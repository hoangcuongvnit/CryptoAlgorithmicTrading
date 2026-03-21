using CryptoTrading.Shared.Session;

namespace RiskGuard.API.Rules;

public sealed class NoCrossSessionCarryRule : IRiskRule
{
    private readonly SessionClock _sessionClock;
    private readonly SessionSettings _settings;

    public NoCrossSessionCarryRule(SessionClock sessionClock, Microsoft.Extensions.Options.IOptions<SessionSettings> settings)
    {
        _sessionClock = sessionClock;
        _settings = settings.Value;
    }

    public string Name => "NoCrossSessionCarry";

    public ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        if (!_settings.Enabled || string.IsNullOrEmpty(context.SessionId))
            return ValueTask.FromResult(RuleResult.Pass());

        var currentSession = _sessionClock.GetCurrentSession();

        if (context.SessionId != currentSession.SessionId)
        {
            return ValueTask.FromResult(RuleResult.Reject(
                $"Cross-session order rejected. Current session: {currentSession.SessionId}, Order session: {context.SessionId}"));
        }

        return ValueTask.FromResult(RuleResult.Pass());
    }
}
