using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly RetailPOSDbContext _context;

    public RoleRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Role?> GetByIdAsync(long id)
    {
        return await _context.Roles
            .Include(r => r.Users.Where(u => u.IsActive))
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<Role?> GetByNameAsync(string name)
    {
        return await _context.Roles
            .Include(r => r.Users.Where(u => u.IsActive))
            .FirstOrDefaultAsync(r => r.Name == name);
    }

}
