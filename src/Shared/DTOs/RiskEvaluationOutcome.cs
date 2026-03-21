namespace CryptoTrading.Shared.DTOs;

public enum RiskEvaluationOutcome
{
    /// <summary>All rules passed without modification.</summary>
    Safe,

    /// <summary>All rules passed but quantity was adjusted (e.g. position size capped).</summary>
    Risk,

    /// <summary>At least one rule rejected the order.</summary>
    Rejected
}
