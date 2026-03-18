using Microsoft.Extensions.Options;
using RiskGuard.API.Configuration;

namespace RiskGuard.API.Rules;

/// <summary>Rejects orders for symbols not in the configured allow-list.</summary>
public sealed class SymbolAllowListRule : IRiskRule
{
    private readonly RiskSettings _settings;

    public string Name => nameof(SymbolAllowListRule);

    public SymbolAllowListRule(IOptions<RiskSettings> settings)
        => _settings = settings.Value;

    public ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.Symbol))
            return ValueTask.FromResult(RuleResult.Reject("Symbol is required."));

        if (_settings.AllowedSymbols.Count > 0 &&
            !_settings.AllowedSymbols.Contains(context.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(
                RuleResult.Reject($"Symbol {context.Symbol} is not in the allowed list."));
        }

        return ValueTask.FromResult(RuleResult.Pass());
    }
}
