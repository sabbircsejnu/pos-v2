using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IInventoryReportReadStore
{
    Task<IReadOnlyList<StockLevelReadModel>> GetStockLevelsAsync(
        InventoryReportReadScope scope,
        long? locationId,
        string? locationType,
        bool lowStockOnly,
        CancellationToken cancellationToken);

    Task<InventoryReportValuationReadModel> GetValuationAsync(
        InventoryReportReadScope scope,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SlowMovingItemReadModel>> GetSlowMovingItemsAsync(
        InventoryReportReadScope scope,
        int days,
        CancellationToken cancellationToken);

    Task<CurrentStockReportReadModel> GetCurrentStockAsync(
        InventoryReportReadScope scope,
        CurrentStockReportFilter filter,
        CancellationToken cancellationToken);
}
