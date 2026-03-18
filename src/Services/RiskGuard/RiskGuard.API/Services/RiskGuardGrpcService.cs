using Grpc.Core;
using RiskGuard.API.Protos;

namespace RiskGuard.API.Services;

public sealed class RiskGuardGrpcService : RiskGuardService.RiskGuardServiceBase
{
    private readonly RiskValidationEngine _validationEngine;
    private readonly ILogger<RiskGuardGrpcService> _logger;

    public RiskGuardGrpcService(
        RiskValidationEngine validationEngine,
        ILogger<RiskGuardGrpcService> logger)
    {
        _validationEngine = validationEngine;
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
            context.CancellationToken);

        if (!result.IsApproved)
        {
            _logger.LogInformation(
                "RiskGuard rejected {Symbol} {Side}: {Reason}",
                request.Symbol, request.Side, result.RejectionReason);
        }

        return new ValidateOrderReply
        {
            IsApproved = result.IsApproved,
            RejectionReason = result.RejectionReason,
            AdjustedQuantity = (double)result.AdjustedQuantity
        };
    }
}
