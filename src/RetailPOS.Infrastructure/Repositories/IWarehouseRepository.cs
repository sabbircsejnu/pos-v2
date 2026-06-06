using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IWarehouseRepository
{
    Task<IEnumerable<Warehouse>> GetAllAsync(long? businessId = null);
    Task<Warehouse?> GetByIdAsync(long id, long? businessId = null);
    Task<Warehouse?> GetByIdWithDetailsAsync(long id, long? businessId = null);
    Task<Warehouse> CreateAsync(Warehouse warehouse);
    Task<Warehouse> UpdateAsync(Warehouse warehouse);
    Task<bool> DeleteAsync(long id, long? businessId = null);
    Task<bool> ExistsAsync(long id, long? businessId = null);
    Task<bool> ExistsByNameAsync(string name, long? excludeId = null, long? businessId = null);
    Task<IEnumerable<Warehouse>> SearchAsync(string searchTerm, long? businessId = null);
    Task<int> GetPurchaseOrderCountAsync(long warehouseId, long? businessId = null);
}
