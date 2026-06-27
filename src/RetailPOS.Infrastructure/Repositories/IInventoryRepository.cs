using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Interface for inventory repository operations
/// </summary>
public interface IInventoryRepository
{
    Task<Inventory?> GetByIdAsync(long id);
    Task<List<Inventory>> GetAllAsync();
    Task<Inventory?> GetByVariantAndLocationAsync(long variantId, long locationId, string locationType);
    Task<List<Inventory>> GetByOutletAsync(long outletId);
    Task<List<Inventory>> GetByWarehouseAsync(long warehouseId);
    Task<List<Inventory>> GetLowStockAsync(long? outletId = null, long? warehouseId = null);
    Task<List<Inventory>> GetOutOfStockAsync(long? outletId = null, long? warehouseId = null);
    Task<List<Inventory>> GetExpiringSoonAsync(int days = 30, long? outletId = null, long? warehouseId = null);
    Task<List<Inventory>> SearchAsync(string? productSearch, long? variantId, string? variantSearch, long? categoryId, long? outletId, long? warehouseId, bool? lowStockOnly, bool? outOfStockOnly);
    Task<Inventory> CreateAsync(Inventory inventory);
    Task<Inventory> UpdateAsync(Inventory inventory);
    Task DeleteAsync(long id);
    Task<bool> ExistsAsync(long variantId, long locationId, string locationType);
    Task<int> GetTotalQuantityByVariantAsync(long variantId);
}
