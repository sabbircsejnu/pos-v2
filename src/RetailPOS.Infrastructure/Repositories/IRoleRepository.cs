using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(long id);
    Task<Role?> GetByNameAsync(string name);
}
