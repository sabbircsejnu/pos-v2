using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IOutletRepository
{
    Task<IEnumerable<Outlet>> GetAllAsync();
    Task<Outlet?> GetByIdAsync(long id);
    Task<Outlet?> GetByIdWithDetailsAsync(long id);
    Task<Outlet> CreateAsync(Outlet outlet);
    Task<Outlet> UpdateAsync(Outlet outlet);
    Task<bool> DeleteAsync(long id);
    Task<bool> ExistsAsync(long id);
    Task<bool> ExistsByNameAsync(string name, long? excludeId = null);
    Task<IEnumerable<Outlet>> SearchAsync(string searchTerm);
    Task<int> GetUserCountAsync(long outletId);
    Task<IEnumerable<User>> GetOutletUsersAsync(long outletId);
}
