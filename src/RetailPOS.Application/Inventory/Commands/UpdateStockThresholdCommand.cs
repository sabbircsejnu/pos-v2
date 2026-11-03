using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record UpdateStockThresholdCommand(long InventoryId, int ReorderLevel)
    : ICommand<InventoryReadModel?>;

public sealed class UpdateStockThresholdCommandHandler(
    IInventoryWriteStore writeStore,
    IInventoryReadStore readStore) : IRequestHandler<UpdateStockThresholdCommand, InventoryReadModel?>
{
    public async Task<InventoryReadModel?> Handle(
        UpdateStockThresholdCommand request,
        CancellationToken cancellationToken)
    {
        if (!await writeStore.UpdateStockThresholdAsync(
                request.InventoryId,
                request.ReorderLevel,
                cancellationToken))
            return null;

        return await readStore.GetByIdAsync(request.InventoryId, cancellationToken);
    }
}
