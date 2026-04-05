using Grpc.Core;
using RiskGuard.API.Infrastructure;
using RiskGuard.API.Protos;

namespace RiskGuard.API.Services;

public sealed class RiskGuardGrpcService : RiskGuardService.RiskGuardServiceBase
{
    private readonly RiskValidationEngine _validationEngine;
    private readonly IRiskEvaluationRepository _evaluationRepository;
    private readonly ILogger<RiskGuardGrpcService> _logger;

    public RiskGuardGrpcService(
        RiskValidationEngine validationEngine,
        IRiskEvaluationRepository evaluationRepository,
        ILogger<RiskGuardGrpcService> logger)
    {
        _validationEngine = validationEngine;
        _evaluationRepository = evaluationRepository;
        _logger = logger;
    }

    public override async Task<ValidateOrderReply> ValidateOrder(
        ValidateOrderRequest request, ServerCallContext context)
    {
        var result = await _validationEngine.EvaluateAsync(
            request.Symbol,
            request.Side,
            (decimal)request.Quantity,
            (decimal)request.EntryPrice,
            (decimal)request.StopLoss,
            (decimal)request.TakeProfit,
            context.CancellationToken,
            request.SessionId,
            request.SessionPhase,
            request.IsReduceOnly,
            request.Environment);

        if (!result.IsApproved)
        {
            _logger.LogInformation(
                "RiskGuard rejected {Symbol} {Side}: {Reason}",
                request.Symbol, request.Side, result.RejectionReason);
        }

        var correlationId = context.RequestHeaders.GetValue("x-correlation-id")
            ?? result.EvaluationId.ToString();

        _ = PersistEvaluationAsync(request, result, correlationId);

        return new ValidateOrderReply
        {
            IsApproved = result.IsApproved,
            RejectionReason = result.RejectionReason,
            AdjustedQuantity = (double)result.AdjustedQuantity
        };
    }

    private async Task PersistEvaluationAsync(
        ValidateOrderRequest request,
        RiskEvaluationResult result,
        string correlationId)
    {
        try
        {
            var record = new RiskEvaluationRecord
            {
                EvaluationId = result.EvaluationId,
                OrderRequestId = correlationId,
                SessionId = string.IsNullOrEmpty(request.SessionId) ? null : request.SessionId,
                Symbol = request.Symbol,
                Side = request.Side,
                RequestedQuantity = (decimal)request.Quantity,
                RequestedPrice = request.EntryPrice > 0 ? (decimal)request.EntryPrice : null,
                MarketPriceAtEvaluation = request.EntryPrice > 0 ? (decimal)request.EntryPrice : null,
                Outcome = result.Outcome.ToString(),
                FinalReasonCode = result.RuleResults.FirstOrDefault(r => r.Result == "Fail")?.ReasonCode,
                FinalReasonMessage = string.IsNullOrEmpty(result.RejectionReason) ? null : result.RejectionReason,
                AdjustedQuantity = result.IsApproved && result.AdjustedQuantity != (decimal)request.Quantity
                    ? result.AdjustedQuantity : null,
                EvaluatedAtUtc = result.EvaluatedAtUtc,
                EvaluationLatencyMs = result.EvaluationLatencyMs,
                CorrelationId = correlationId
            };

            await _evaluationRepository.SaveAsync(record, result.RuleResults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist risk evaluation {EvaluationId} for {Symbol} — trading decision not affected",
                result.EvaluationId, request.Symbol);
        }
    }
}
