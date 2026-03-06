namespace CryptoTrading.Shared.DTOs;

public sealed record RiskValidationResult
{
    public required bool IsApproved { get; init; }
    public string RejectionReason { get; init; } = string.Empty;
    public decimal AdjustedQuantity { get; init; }
}
