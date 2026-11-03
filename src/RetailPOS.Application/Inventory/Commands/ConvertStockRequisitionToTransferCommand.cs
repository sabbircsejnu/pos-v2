using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record ConvertStockRequisitionToTransferCommand(long Id, long CreatedBy)
    : ICommand<StockTransferReadModel>;

public sealed class ConvertStockRequisitionToTransferCommandHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockRequisitionTransferStore transferStore,
    IStockTransferReadStore transferReadStore)
    : IRequestHandler<ConvertStockRequisitionToTransferCommand, StockTransferReadModel>
{
    public async Task<StockTransferReadModel> Handle(
        ConvertStockRequisitionToTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var transferId = await transferStore.ConvertToTransferAsync(
            request.Id,
            businessId,
            request.CreatedBy,
            cancellationToken);

        return await transferReadStore.GetByIdAsync(transferId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");
    }
}
