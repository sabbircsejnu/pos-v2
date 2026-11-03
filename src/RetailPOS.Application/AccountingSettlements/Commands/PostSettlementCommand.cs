using MediatR;
using RetailPOS.Application.AccountingSettlements.Abstractions;
using RetailPOS.Application.AccountingSettlements.ReadModels;

namespace RetailPOS.Application.AccountingSettlements.Commands;

public sealed record PostSettlementCommand(
    long ActorUserId,
    SettlementPostingInput Input) : IRequest<SettlementBatchReadModel>;

public sealed class PostSettlementCommandHandler(
    ISettlementTransferCommandStore commandStore,
    ISettlementTransferTenantScope tenantScope)
    : IRequestHandler<PostSettlementCommand, SettlementBatchReadModel>
{
    public async Task<SettlementBatchReadModel> Handle(
        PostSettlementCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.PostSettlementAsync(
            businessId,
            request.ActorUserId,
            request.Input,
            cancellationToken);
    }
}
