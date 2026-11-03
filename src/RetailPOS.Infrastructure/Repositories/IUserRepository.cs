using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(long id, long? businessId = null);
    Task<User?> GetByEmailAsync(string email);
    Task<User> CreateAsync(User user);
    Task<User> UpdateAsync(User user);
    Task<bool> DeleteAsync(long id, long? businessId = null);
    Task<bool> ExistsAsync(string email);
    Task<IEnumerable<User>> GetByOutletIdAsync(long outletId, long? businessId = null);
    Task<IEnumerable<User>> GetByRoleIdAsync(long roleId, long? businessId = null);
}
