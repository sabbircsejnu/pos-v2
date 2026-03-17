using RetailPOS.API.DTOs.Inventory;

namespace RetailPOS.API.Services;

/// <summary>
/// Interface for inventory service operations
/// </summary>
public interface IInventoryService
{
    Task<InventoryDto?> GetByIdAsync(long id);
    Task<List<InventoryDto>> GetAllAsync();
    Task<List<InventoryDto>> GetByOutletAsync(long outletId);
    Task<List<InventoryDto>> GetByWarehouseAsync(long warehouseId);
    Task<List<LowStockDto>> GetLowStockAsync(long? outletId = null, long? warehouseId = null);
    Task<List<InventoryDto>> GetOutOfStockAsync(long? outletId = null, long? warehouseId = null);
    Task<List<InventoryDto>> GetExpiringSoonAsync(int days = 30, long? outletId = null, long? warehouseId = null);
    Task<List<InventoryDto>> SearchAsync(InventorySearchDto searchDto);
    Task<InventoryDto> UpdateStockThresholdAsync(long id, UpdateStockThresholdDto dto);
    Task<InventoryValuationDto> GetValuationAsync(long? outletId = null, long? warehouseId = null);
    Task<List<InventoryByLocationDto>> GetInventorySummaryByLocationsAsync();
    Task<int> GetTotalStockByVariantAsync(long variantId);
}
