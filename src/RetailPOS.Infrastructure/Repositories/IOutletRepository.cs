using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IOutletRepository
{
    Task<IEnumerable<Outlet>> GetAllAsync(long? businessId = null);
    Task<Outlet?> GetByIdAsync(long id, long? businessId = null);
    Task<Outlet?> GetByIdWithDetailsAsync(long id, long? businessId = null);
    Task<Outlet> CreateAsync(Outlet outlet);
    Task<Outlet> UpdateAsync(Outlet outlet);
    Task<bool> DeleteAsync(long id, long? businessId = null);
    Task<bool> ExistsAsync(long id, long? businessId = null);
    Task<bool> ExistsByNameAsync(string name, long? excludeId = null, long? businessId = null);
    Task<IEnumerable<Outlet>> SearchAsync(string searchTerm, long? businessId = null);
    Task<int> GetUserCountAsync(long outletId, long? businessId = null);
    Task<IEnumerable<User>> GetOutletUsersAsync(long outletId, long? businessId = null);
}
