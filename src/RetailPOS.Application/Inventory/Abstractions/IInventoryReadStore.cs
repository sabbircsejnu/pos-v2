using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Application.Inventory.ReadModels.Alerts;
using RetailPOS.Application.Inventory.Queries;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IInventoryReadStore
{
    Task<InventoryReadModel?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryReadModel>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryReadModel>> GetByOutletAsync(long outletId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryReadModel>> GetByWarehouseAsync(long warehouseId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryReadModel>> GetOutOfStockAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryReadModel>> GetExpiringSoonAsync(
        int days,
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryLocationSummaryReadModel>> GetSummaryByLocationAsync(
        CancellationToken cancellationToken);
    Task<InventoryValuationReadModel> GetValuationAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<VariantAttributeFilterReadModel>> GetVariantAttributeFilterOptionsAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryReadModel>> SearchAsync(
        InventorySearchCriteria criteria,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<LowStockReadModel>> GetLowStockAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken);
    Task<int> GetTotalQuantityByVariantAsync(long variantId, CancellationToken cancellationToken);
}
