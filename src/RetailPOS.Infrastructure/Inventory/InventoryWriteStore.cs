using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.Infrastructure.InventoryStores;

public sealed class InventoryWriteStore(
    IInventoryRepository inventoryRepository,
    ILogger<InventoryWriteStore> logger) : IInventoryWriteStore
{
    public async Task<bool> UpdateStockThresholdAsync(
        long id,
        int reorderLevel,
        CancellationToken cancellationToken)
    {
        var inventory = await inventoryRepository.GetByIdAsync(id, cancellationToken);
        if (inventory is null)
            return false;

        inventory.LowStockThreshold = reorderLevel;
        await inventoryRepository.UpdateAsync(inventory, cancellationToken);

        logger.LogInformation(
            "Updated stock threshold for inventory {InventoryId}: Reorder={ReorderLevel}",
            id,
            reorderLevel);

        return true;
    }
}
