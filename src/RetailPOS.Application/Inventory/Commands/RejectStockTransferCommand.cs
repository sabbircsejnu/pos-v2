using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record RejectStockTransferCommand(long Id, string? Reason, long? RejectedBy)
    : ICommand<StockTransferReadModel>;

public sealed class RejectStockTransferCommandHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferRejectionStore rejectionStore,
    IStockTransferReadStore readStore)
    : IRequestHandler<RejectStockTransferCommand, StockTransferReadModel>
{
    public async Task<StockTransferReadModel> Handle(
        RejectStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var rejectedId = await rejectionStore.RejectAsync(
            request.Id,
            businessId,
            request.Reason,
            request.RejectedBy,
            cancellationToken);

        return await readStore.GetByIdAsync(rejectedId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock transfer with ID {rejectedId} not found");
    }
}
