using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record ReceiveStockTransferCommand(
    long Id,
    StockTransferReceiveInput? Input,
    long? ReceivedBy)
    : ICommand<StockTransferReadModel>;

public sealed class ReceiveStockTransferCommandHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferReceiveStore receiveStore,
    IStockTransferReceiveCacheInvalidator cacheInvalidator,
    IStockTransferReadStore readStore)
    : IRequestHandler<ReceiveStockTransferCommand, StockTransferReadModel>
{
    public async Task<StockTransferReadModel> Handle(
        ReceiveStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var result = await receiveStore.ReceiveAsync(
            request.Id,
            businessId,
            request.Input,
            request.ReceivedBy,
            cancellationToken);

        if (result.ToLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var variantId in result.VariantIds)
            {
                await cacheInvalidator.InvalidateAsync(
                    variantId,
                    result.ToLocationId,
                    cancellationToken);
            }
        }

        return await readStore.GetByIdAsync(result.TransferId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock transfer with ID {result.TransferId} not found");
    }
}
