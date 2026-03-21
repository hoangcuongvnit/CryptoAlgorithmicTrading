namespace CryptoTrading.Shared.DTOs;

/// <summary>Per-rule result returned in the evaluation detail API response.</summary>
public sealed record RiskRuleResultDto(
    string RuleName,
    string RuleVersion,
    string Result,           // "Pass" | "Fail" | "Adjusted" | "Skipped"
    string? ReasonCode,
    string? ReasonMessage,
    string? ThresholdValue,
    string? ActualValue,
    long DurationMs,
    int SequenceOrder);

/// <summary>Full evaluation record returned by the query API.</summary>
public sealed record RiskEvaluationDto
{
    public Guid EvaluationId { get; init; }
    public string OrderRequestId { get; init; } = string.Empty;
    public string? SessionId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public decimal RequestedQuantity { get; init; }
    public decimal? RequestedPrice { get; init; }
    public decimal? MarketPriceAtEvaluation { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public string? FinalReasonCode { get; init; }
    public string? FinalReasonMessage { get; init; }
    public decimal? AdjustedQuantity { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public long EvaluationLatencyMs { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<RiskRuleResultDto> RuleResults { get; init; } = [];
}
