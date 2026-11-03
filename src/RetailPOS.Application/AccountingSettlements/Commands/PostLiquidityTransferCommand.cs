using MediatR;
using RetailPOS.Application.AccountingSettlements.Abstractions;
using RetailPOS.Application.AccountingSettlements.ReadModels;

namespace RetailPOS.Application.AccountingSettlements.Commands;

public sealed record PostLiquidityTransferCommand(
    long ActorUserId,
    LiquidityTransferPostingInput Input) : IRequest<LiquidityTransferReadModel>;

public sealed class PostLiquidityTransferCommandHandler(
    ISettlementTransferCommandStore commandStore,
    ISettlementTransferTenantScope tenantScope)
    : IRequestHandler<PostLiquidityTransferCommand, LiquidityTransferReadModel>
{
    public async Task<LiquidityTransferReadModel> Handle(
        PostLiquidityTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.PostTransferAsync(
            businessId,
            request.ActorUserId,
            request.Input,
            cancellationToken);
    }
}
