using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(long id);
    Task<User?> GetByEmailAsync(string email);
    Task<IEnumerable<User>> GetAllAsync(int pageNumber = 1, int pageSize = 10, string? searchQuery = null, long? roleId = null, long? outletId = null, bool? isActive = null);
    Task<int> GetTotalCountAsync(string? searchQuery = null, long? roleId = null, long? outletId = null, bool? isActive = null);
    Task<User> CreateAsync(User user);
    Task<User> UpdateAsync(User user);
    Task<bool> DeleteAsync(long id);
    Task<bool> ExistsAsync(string email);
    Task<IEnumerable<User>> GetByOutletIdAsync(long outletId);
    Task<IEnumerable<User>> GetByRoleIdAsync(long roleId);
}
