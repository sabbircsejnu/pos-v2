using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IWarehouseRepository
{
    Task<IEnumerable<Warehouse>> GetAllAsync();
    Task<Warehouse?> GetByIdAsync(long id);
    Task<Warehouse?> GetByIdWithDetailsAsync(long id);
    Task<Warehouse> CreateAsync(Warehouse warehouse);
    Task<Warehouse> UpdateAsync(Warehouse warehouse);
    Task<bool> DeleteAsync(long id);
    Task<bool> ExistsAsync(long id);
    Task<bool> ExistsByNameAsync(string name, long? excludeId = null);
    Task<IEnumerable<Warehouse>> SearchAsync(string searchTerm);
    Task<int> GetPurchaseOrderCountAsync(long warehouseId);
}
