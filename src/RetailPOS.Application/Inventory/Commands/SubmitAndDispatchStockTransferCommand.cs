using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record SubmitAndDispatchStockTransferCommand(long Id, long? UserId, bool AutoApprove)
    : ICommand<StockTransferReadModel>;

public sealed class SubmitAndDispatchStockTransferCommandHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferDispatchStore dispatchStore,
    IStockTransferReadStore readStore,
    IStockTransferDispatchCacheInvalidator cacheInvalidator)
    : IRequestHandler<SubmitAndDispatchStockTransferCommand, StockTransferReadModel>
{
    public async Task<StockTransferReadModel> Handle(
        SubmitAndDispatchStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var result = await dispatchStore.SubmitAndDispatchAsync(
            request.Id,
            businessId,
            request.UserId,
            request.AutoApprove,
            cancellationToken);

        if (result.WasDispatched
            && result.FromLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
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
