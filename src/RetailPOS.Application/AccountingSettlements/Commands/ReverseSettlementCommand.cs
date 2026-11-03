using MediatR;
using RetailPOS.Application.AccountingSettlements.Abstractions;
using RetailPOS.Application.AccountingSettlements.ReadModels;

namespace RetailPOS.Application.AccountingSettlements.Commands;

public sealed record ReverseSettlementCommand(
    long SettlementId,
    long ActorUserId,
    SettlementReversalInput Input) : IRequest<SettlementBatchReadModel>;

public sealed class ReverseSettlementCommandHandler(
    ISettlementTransferCommandStore commandStore,
    ISettlementTransferTenantScope tenantScope)
    : IRequestHandler<ReverseSettlementCommand, SettlementBatchReadModel>
{
    public async Task<SettlementBatchReadModel> Handle(
        ReverseSettlementCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await commandStore.ReverseSettlementAsync(
            businessId,
            request.ActorUserId,
            request.SettlementId,
            request.Input,
            cancellationToken);
    }
}
