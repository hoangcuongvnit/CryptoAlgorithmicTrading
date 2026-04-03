using System.Diagnostics;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Timeline;
using RiskGuard.API.Rules;

namespace RiskGuard.API.Services;

/// <summary>
/// Runs each registered <see cref="IRiskRule"/> in order.
/// The first rejection short-circuits the chain; skipped rules are recorded as "Skipped".
/// Quantity adjustments from one rule are propagated to all subsequent rules.
/// Every evaluation produces a full <see cref="RiskEvaluationResult"/> with per-rule details,
/// EvaluationId, outcome, and latency for persistence and audit.
/// </summary>
public sealed class RiskValidationEngine
{
    private readonly IReadOnlyList<IRiskRule> _rules;
    private readonly ValidationHistory _history;
    private readonly ITimelineEventPublisher _timeline;
    private readonly ILogger<RiskValidationEngine> _logger;

    public RiskValidationEngine(
        IEnumerable<IRiskRule> rules,
        ValidationHistory history,
        ITimelineEventPublisher timeline,
        ILogger<RiskValidationEngine> logger)
    {
        _rules = rules.ToList();
        _history = history;
        _timeline = timeline;
        _logger = logger;
    }

    public async Task<RiskEvaluationResult> EvaluateAsync(
        string symbol,
        string side,
        decimal quantity,
        decimal entryPrice,
        decimal stopLoss,
        decimal takeProfit,
        CancellationToken ct = default,
        string? sessionId = null,
        string? sessionPhase = null,
        bool isReduceOnly = false)
    {
        var totalSw = Stopwatch.StartNew();
        var effectiveQty = quantity;
        var ruleDetails = new List<RuleEvaluationDetail>(_rules.Count);

        for (var i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            var context = new RiskContext(symbol, side, effectiveQty, entryPrice, stopLoss, takeProfit,
                sessionId, sessionPhase, isReduceOnly);

            var ruleStart = Stopwatch.GetTimestamp();
            var result = await rule.EvaluateAsync(context, ct);
            var ruleDurationMs = (long)((Stopwatch.GetTimestamp() - ruleStart) * 1000.0 / Stopwatch.Frequency);

            if (result.IsRejected)
            {
                ruleDetails.Add(new RuleEvaluationDetail(
                    rule.Name, rule.Version, "Fail",
                    result.ReasonCode, result.Reason,
                    result.ThresholdValue, result.ActualValue,
                    ruleDurationMs, i));

                // Mark remaining rules as Skipped
                for (var j = i + 1; j < _rules.Count; j++)
                    ruleDetails.Add(new RuleEvaluationDetail(
                        _rules[j].Name, _rules[j].Version, "Skipped",
                        null, null, null, null, 0, j));

                totalSw.Stop();
                _logger.LogDebug("[{Rule}] rejected {Symbol} {Side}: {Reason}",
                    rule.Name, symbol, side, result.Reason);
                _history.Record(symbol, side, approved: false, rejectionReason: result.Reason);
                _ = _timeline.PublishAsync(new TimelineEvent
                {
                    SourceService = "RiskGuard",
                    EventType = TimelineEventTypes.RiskValidationRejected,
                    Symbol = symbol,
                    SessionId = sessionId,
                    Severity = TimelineSeverity.Warning,
                    Payload = new Dictionary<string, object?>
                    {
                        ["rule"] = rule.Name,
                        ["reason"] = result.Reason,
                        ["side"] = side,
                        ["quantity"] = quantity,
                        ["entry_price"] = entryPrice,
                    },
                    Tags = ["rejected", rule.Name.ToLowerInvariant()],
                }, ct);
                return RiskEvaluationResult.Reject(result.Reason, ruleDetails, totalSw.ElapsedMilliseconds);
            }

            if (result.AdjustedQuantity.HasValue)
            {
                _logger.LogDebug("[{Rule}] adjusted qty for {Symbol}: {Old} → {New}",
                    rule.Name, symbol, effectiveQty, result.AdjustedQuantity.Value);
                var prevQty = effectiveQty;
                effectiveQty = result.AdjustedQuantity.Value;
                ruleDetails.Add(new RuleEvaluationDetail(
                    rule.Name, rule.Version, "Adjusted",
                    result.ReasonCode,
                    $"Quantity adjusted from {prevQty} to {effectiveQty}",
                    result.ThresholdValue, result.ActualValue ?? effectiveQty.ToString("G"),
                    ruleDurationMs, i));
            }
            else
            {
                ruleDetails.Add(new RuleEvaluationDetail(
                    rule.Name, rule.Version, "Pass",
                    null, null, null, null, ruleDurationMs, i));
            }
        }

        totalSw.Stop();
        _history.Record(symbol, side, approved: true, rejectionReason: string.Empty);
        _ = _timeline.PublishAsync(new TimelineEvent
        {
            SourceService = "RiskGuard",
            EventType = TimelineEventTypes.RiskValidationApproved,
            Symbol = symbol,
            SessionId = sessionId,
            Severity = TimelineSeverity.Info,
            Payload = new Dictionary<string, object?>
            {
                ["side"] = side,
                ["quantity"] = effectiveQty,
                ["entry_price"] = entryPrice,
                ["latency_ms"] = totalSw.ElapsedMilliseconds,
            },
            Tags = ["approved"],
        }, ct);
        return RiskEvaluationResult.Approve(effectiveQty, effectiveQty != quantity, ruleDetails, totalSw.ElapsedMilliseconds);
    }
}

/// <summary>Per-rule evaluation detail captured during engine execution.</summary>
public sealed record RuleEvaluationDetail(
    string RuleName,
    string RuleVersion,
    string Result,           // "Pass" | "Fail" | "Adjusted" | "Skipped"
    string? ReasonCode,
    string? ReasonMessage,
    string? ThresholdValue,
    string? ActualValue,
    long DurationMs,
    int SequenceOrder);

public sealed record RiskEvaluationResult
{
    public bool IsApproved { get; init; }
    public string RejectionReason { get; init; } = string.Empty;
    public decimal AdjustedQuantity { get; init; }
    public Guid EvaluationId { get; init; }
    public RiskEvaluationOutcome Outcome { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public long EvaluationLatencyMs { get; init; }
    public IReadOnlyList<RuleEvaluationDetail> RuleResults { get; init; } = [];

    public static RiskEvaluationResult Approve(
        decimal adjustedQuantity,
        bool wasAdjusted,
        IReadOnlyList<RuleEvaluationDetail> ruleResults,
        long latencyMs)
        => new()
        {
            IsApproved = true,
            AdjustedQuantity = adjustedQuantity,
            Outcome = wasAdjusted ? RiskEvaluationOutcome.Risk : RiskEvaluationOutcome.Safe,
            EvaluationId = Guid.NewGuid(),
            EvaluatedAtUtc = DateTime.UtcNow,
            EvaluationLatencyMs = latencyMs,
            RuleResults = ruleResults
        };

    public static RiskEvaluationResult Reject(
        string reason,
        IReadOnlyList<RuleEvaluationDetail> ruleResults,
        long latencyMs)
        => new()
        {
            IsApproved = false,
            RejectionReason = reason,
            Outcome = RiskEvaluationOutcome.Rejected,
            EvaluationId = Guid.NewGuid(),
            EvaluatedAtUtc = DateTime.UtcNow,
            EvaluationLatencyMs = latencyMs,
            RuleResults = ruleResults
        };
}
