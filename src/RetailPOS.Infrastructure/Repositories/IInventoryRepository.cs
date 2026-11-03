using RetailPOS.Core.Entities;
using RetailPOS.Application.Inventory.Queries;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Interface for inventory repository operations
/// </summary>
public interface IInventoryRepository
{
    Task<Inventory?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<List<Inventory>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Inventory?> GetByVariantAndLocationAsync(long variantId, long locationId, string locationType);
    Task<List<Inventory>> GetByOutletAsync(long outletId, CancellationToken cancellationToken = default);
    Task<List<Inventory>> GetByWarehouseAsync(long warehouseId, CancellationToken cancellationToken = default);
    Task<List<Inventory>> GetLowStockAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken = default);
    Task<List<Inventory>> GetOutOfStockAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken = default);
    Task<List<Inventory>> GetExpiringSoonAsync(
        int days,
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken = default);
    Task<List<Inventory>> SearchAsync(
        string? productSearch,
        long? variantId,
        string? variantSearch,
        long? categoryId,
        InventoryLocationScope locationScope,
        bool? lowStockOnly,
        bool? outOfStockOnly,
        CancellationToken cancellationToken = default);
    Task<Inventory> CreateAsync(Inventory inventory);
    Task<Inventory> UpdateAsync(Inventory inventory, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id);
    Task<bool> ExistsAsync(long variantId, long locationId, string locationType);
    Task<int> GetTotalQuantityByVariantAsync(long variantId, CancellationToken cancellationToken = default);
}
