using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record SendStockTransferCommand(long Id, long? UserId)
    : ICommand<StockTransferReadModel>;

public sealed class SendStockTransferCommandHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferDispatchStore dispatchStore,
    IStockTransferReadStore readStore,
    IStockTransferDispatchCacheInvalidator cacheInvalidator)
    : IRequestHandler<SendStockTransferCommand, StockTransferReadModel>
{
    public async Task<StockTransferReadModel> Handle(
        SendStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var result = await dispatchStore.DispatchApprovedAsync(
            request.Id,
            businessId,
            request.UserId,
            cancellationToken);

        if (result.FromLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var variantId in result.VariantIds)
            {
                await cacheInvalidator.InvalidateAsync(
                    variantId,
                    result.FromLocationId,
                    cancellationToken);
            }
        }

        return await readStore.GetByIdAsync(result.TransferId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock transfer with ID {result.TransferId} not found");
    }
}
