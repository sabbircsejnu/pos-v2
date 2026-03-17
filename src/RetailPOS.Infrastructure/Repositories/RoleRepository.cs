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
            .Include(r => r.Users)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<Role?> GetByNameAsync(string name)
    {
        return await _context.Roles
            .Include(r => r.Users)
            .FirstOrDefaultAsync(r => r.Name == name);
    }

    public async Task<IEnumerable<Role>> GetAllAsync()
    {
        return await _context.Roles
            .Include(r => r.Users)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<Role> CreateAsync(Role role)
    {
        role.CreatedAt = DateTime.UtcNow;
        role.UpdatedAt = DateTime.UtcNow;
        
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();
        
        return (await GetByIdAsync(role.Id))!;
    }

    public async Task<Role> UpdateAsync(Role role)
    {
        role.UpdatedAt = DateTime.UtcNow;
        
        _context.Roles.Update(role);
        await _context.SaveChangesAsync();
        
        return (await GetByIdAsync(role.Id))!;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var role = await _context.Roles.FindAsync(id);
        if (role == null)
        {
            return false;
        }

        _context.Roles.Remove(role);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(string name)
    {
        return await _context.Roles.AnyAsync(r => r.Name == name);
    }

    public async Task<bool> IsRoleInUseAsync(long id)
    {
        return await _context.Users.AnyAsync(u => u.RoleId == id);
    }
}
