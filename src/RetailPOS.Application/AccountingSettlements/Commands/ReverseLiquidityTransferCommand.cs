using MediatR;
using RetailPOS.Application.AccountingSettlements.Abstractions;
using RetailPOS.Application.AccountingSettlements.ReadModels;

namespace RetailPOS.Application.AccountingSettlements.Commands;

public sealed record ReverseLiquidityTransferCommand(
    long TransferId,
    long ActorUserId,
    LiquidityTransferReversalInput Input) : IRequest<LiquidityTransferReadModel>;

public sealed class ReverseLiquidityTransferCommandHandler(
    ISettlementTransferCommandStore commandStore,
    ISettlementTransferTenantScope tenantScope)
    : IRequestHandler<ReverseLiquidityTransferCommand, LiquidityTransferReadModel>
{
    public async Task<LiquidityTransferReadModel> Handle(
        ReverseLiquidityTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.ReverseTransferAsync(
            businessId,
            request.ActorUserId,
            request.TransferId,
            request.Input,
            cancellationToken);
    }
}
