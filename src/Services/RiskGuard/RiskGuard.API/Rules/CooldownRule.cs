using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using RiskGuard.API.Configuration;

namespace RiskGuard.API.Rules;

/// <summary>
/// Enforces a minimum time gap between consecutive orders on the same symbol.
/// Cooldown state is in-memory and resets on service restart.
/// </summary>
public sealed class CooldownRule : IRiskRule
{
    private readonly RiskSettings _settings;
    private readonly ConcurrentDictionary<string, DateTime> _lastOrderTime =
        new(StringComparer.OrdinalIgnoreCase);

    public string Name => nameof(CooldownRule);

    public CooldownRule(IOptions<RiskSettings> settings)
        => _settings = settings.Value;

    public ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        if (_settings.CooldownSeconds <= 0)
            return ValueTask.FromResult(RuleResult.Pass());

        var now = DateTime.UtcNow;

        if (_lastOrderTime.TryGetValue(context.Symbol, out var lastAt))
        {
            var elapsed = now - lastAt;
            if (elapsed < TimeSpan.FromSeconds(_settings.CooldownSeconds))
            {
                var remaining = (int)Math.Ceiling(_settings.CooldownSeconds - elapsed.TotalSeconds);
                return ValueTask.FromResult(RuleResult.Reject(
                    $"Cooldown active for {context.Symbol}. Retry in {remaining}s."));
            }
        }

        // Record approval timestamp so subsequent requests are gated
        _lastOrderTime[context.Symbol] = now;
        return ValueTask.FromResult(RuleResult.Pass());
    }

    /// <summary>Returns symbols that are currently within the cooldown window.</summary>
    public IReadOnlyList<CooldownInfo> GetActiveCooldowns()
    {
        if (_settings.CooldownSeconds <= 0) return [];

        var now = DateTime.UtcNow;
        var cooldown = TimeSpan.FromSeconds(_settings.CooldownSeconds);
        var result = new List<CooldownInfo>();

        foreach (var (symbol, lastAt) in _lastOrderTime)
        {
            var elapsed = now - lastAt;
            if (elapsed < cooldown)
            {
                var remaining = (int)Math.Ceiling(cooldown.TotalSeconds - elapsed.TotalSeconds);
                result.Add(new CooldownInfo(symbol, lastAt, remaining));
            }
        }

        return result;
    }
}

public sealed record CooldownInfo(string Symbol, DateTime LastOrderUtc, int RemainingSeconds);
