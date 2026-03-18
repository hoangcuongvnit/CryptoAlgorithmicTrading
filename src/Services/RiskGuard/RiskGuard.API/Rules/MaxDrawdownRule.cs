using CryptoTrading.Shared.DTOs;
using Microsoft.Extensions.Options;
using RiskGuard.API.Configuration;
using RiskGuard.API.Infrastructure;

namespace RiskGuard.API.Rules;

/// <summary>
/// Halts all trading for the day when the session's net P&amp;L loss exceeds a configured
/// percentage of the virtual account balance.
///
/// P&amp;L is approximated as: Σ(sell revenue) − Σ(buy cost) for today's successful orders.
/// A DB query failure never blocks trading — it is logged and the rule passes.
/// On first breach each calendar day, publishes a <see cref="SystemEventType.MaxDrawdownBreached"/>
/// event to the <c>system:events</c> Redis channel.
/// </summary>
public sealed class MaxDrawdownRule : IRiskRule
{
    private readonly RiskSettings _settings;
    private readonly OrderStatsRepository _repository;
    private readonly SystemEventPublisher _eventPublisher;
    private readonly ILogger<MaxDrawdownRule> _logger;

    private DateOnly _breachNotifiedDate = DateOnly.MinValue;
    private readonly object _breachLock = new();

    public string Name => nameof(MaxDrawdownRule);

    public MaxDrawdownRule(
        IOptions<RiskSettings> settings,
        OrderStatsRepository repository,
        SystemEventPublisher eventPublisher,
        ILogger<MaxDrawdownRule> logger)
    {
        _settings = settings.Value;
        _repository = repository;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async ValueTask<RuleResult> EvaluateAsync(RiskContext context, CancellationToken ct = default)
    {
        if (_settings.MaxDrawdownPercent <= 0 || _settings.VirtualAccountBalance <= 0)
            return RuleResult.Pass();

        try
        {
            var dailyPnl = await _repository.GetDailyNetPnlAsync(_settings.PaperTradingOnly, ct);

            var maxLoss = -(_settings.MaxDrawdownPercent / 100m * _settings.VirtualAccountBalance);

            if (dailyPnl < maxLoss)
            {
                _logger.LogWarning(
                    "MaxDrawdown breached: daily P&L {PnL:F2} USDT is below limit {Limit:F2} USDT",
                    dailyPnl, maxLoss);

                NotifyBreachOnce(dailyPnl, maxLoss);

                return RuleResult.Reject(
                    $"Daily drawdown limit reached. Net P&L: {dailyPnl:F2} USDT " +
                    $"(limit: {maxLoss:F2} USDT). All trading halted for the day.");
            }

            _logger.LogDebug("MaxDrawdown OK: daily P&L {PnL:F2} / limit {Limit:F2}", dailyPnl, maxLoss);
        }
        catch (Exception ex)
        {
            // A monitoring failure must never stop trading
            _logger.LogError(ex, "MaxDrawdownRule DB query failed — skipping drawdown check");
        }

        return RuleResult.Pass();
    }

    private void NotifyBreachOnce(decimal dailyPnl, decimal maxLoss)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        bool shouldNotify;

        lock (_breachLock)
        {
            shouldNotify = _breachNotifiedDate != today;
            if (shouldNotify) _breachNotifiedDate = today;
        }

        if (!shouldNotify) return;

        _ = _eventPublisher.PublishAsync(new SystemEvent
        {
            Type = SystemEventType.MaxDrawdownBreached,
            ServiceName = "RiskGuard",
            Message = $"Daily P&L {dailyPnl:F2} USDT breached limit {maxLoss:F2} USDT. " +
                      $"All new orders will be rejected until midnight UTC.",
            Timestamp = DateTime.UtcNow
        });
    }
}
