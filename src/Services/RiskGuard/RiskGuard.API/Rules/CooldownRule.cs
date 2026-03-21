using Microsoft.Extensions.Options;
using RiskGuard.API.Configuration;
using RiskGuard.API.Infrastructure;
using System.Collections.Concurrent;

namespace RiskGuard.API.Rules;

/// <summary>
/// Enforces a minimum time gap between consecutive orders on the same symbol.
/// Cooldown state is in-memory and resets on service restart.
/// </summary>
public sealed class CooldownRule : IRiskRule
{
    private readonly RiskSettings _settings;
    private readonly IRedisPersistenceService _redis;
    private readonly ILogger<CooldownRule> _logger;
    private readonly Task _initializeTask;
    private readonly ConcurrentDictionary<string, DateTime> _lastOrderTime =
        new(StringComparer.OrdinalIgnoreCase);

    public string Name => nameof(CooldownRule);

    public CooldownRule(
        IOptions<RiskSettings> settings,
        IRedisPersistenceService redis,
        ILogger<CooldownRule> logger)
    {
        _settings = settings.Value;
        _redis = redis;
        _logger = logger;
        _initializeTask = InitializeFromRedisAsync();
    }

    public ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        ObserveInitializationFailure();

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
        _ = PersistCooldownAsync(context.Symbol, now);

        return ValueTask.FromResult(RuleResult.Pass());
    }

    /// <summary>Returns the total number of symbols that have a cooldown entry (active or expired).</summary>
    public int GetStoredCooldownCount() => _lastOrderTime.Count;

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

    private void ObserveInitializationFailure()
    {
        if (_initializeTask.IsFaulted)
            _ = _initializeTask.Exception;
    }

    private async Task InitializeFromRedisAsync()
    {
        try
        {
            var loaded = await _redis.LoadAllCooldownsAsync(CancellationToken.None);
            foreach (var (symbol, timestampUtc) in loaded)
            {
                _lastOrderTime.AddOrUpdate(
                    symbol,
                    timestampUtc,
                    (_, existing) => existing >= timestampUtc ? existing : timestampUtc);
            }

            var activeCount = GetActiveCooldowns().Count;
            _logger.LogInformation(
                "Loaded {Total} cooldown entries from Redis ({Active} active within {CooldownSeconds}s window)",
                loaded.Count, activeCount, _settings.CooldownSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize cooldowns from Redis; starting with in-memory state");
        }
    }

    private async Task PersistCooldownAsync(string symbol, DateTime timestampUtc)
    {
        try
        {
            await _redis.SetCooldownAsync(symbol, timestampUtc, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist cooldown for {Symbol}", symbol);
        }
    }
}

public sealed record CooldownInfo(string Symbol, DateTime LastOrderUtc, int RemainingSeconds);
