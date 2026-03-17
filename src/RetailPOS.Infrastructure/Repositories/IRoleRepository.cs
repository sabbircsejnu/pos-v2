using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(long id);
    Task<Role?> GetByNameAsync(string name);
    Task<IEnumerable<Role>> GetAllAsync();
    Task<Role> CreateAsync(Role role);
    Task<Role> UpdateAsync(Role role);
    Task<bool> DeleteAsync(long id);
    Task<bool> ExistsAsync(string name);
    Task<bool> IsRoleInUseAsync(long id);
}
