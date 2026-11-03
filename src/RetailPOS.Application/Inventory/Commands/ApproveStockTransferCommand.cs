using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record ApproveStockTransferCommand(long Id, long? ApprovedBy)
    : ICommand<StockTransferReadModel>;

public sealed class ApproveStockTransferCommandHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferApprovalStore approvalStore,
    IStockTransferReadStore readStore)
    : IRequestHandler<ApproveStockTransferCommand, StockTransferReadModel>
{
    public async Task<StockTransferReadModel> Handle(
        ApproveStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var approvedId = await approvalStore.ApproveAsync(
            request.Id,
            businessId,
            request.ApprovedBy,
            cancellationToken);

        return await readStore.GetByIdAsync(approvedId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock transfer with ID {approvedId} not found");
    }
}
