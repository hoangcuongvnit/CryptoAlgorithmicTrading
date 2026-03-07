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

    public override Task<ValidateOrderReply> ValidateOrder(ValidateOrderRequest request, ServerCallContext context)
    {
        var result = _validationEngine.Evaluate(
            request.Symbol,
            request.Side,
            (decimal)request.Quantity,
            (decimal)request.EntryPrice,
            (decimal)request.StopLoss,
            (decimal)request.TakeProfit);

        if (!result.IsApproved)
        {
            _logger.LogInformation(
                "RiskGuard rejected {Symbol}: {Reason}",
                request.Symbol,
                result.RejectionReason);
        }

        return Task.FromResult(new ValidateOrderReply
        {
            IsApproved = result.IsApproved,
            RejectionReason = result.RejectionReason,
            AdjustedQuantity = (double)result.AdjustedQuantity
        });
    }
}
