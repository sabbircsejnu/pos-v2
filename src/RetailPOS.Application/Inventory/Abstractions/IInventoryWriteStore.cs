namespace RetailPOS.Application.Inventory.Abstractions;

public interface IInventoryWriteStore
{
    Task<bool> UpdateStockThresholdAsync(
        long id,
        int reorderLevel,
        CancellationToken cancellationToken);
}
