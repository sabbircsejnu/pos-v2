using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly RetailPOSDbContext _context;

    public UserRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(long id)
    {
        return await _context.Users
            .Include(u => u.Role)
            .Include(u => u.Outlet)
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .Include(u => u.Role)
            .Include(u => u.Outlet)
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<IEnumerable<User>> GetAllAsync(
        int pageNumber = 1,
        int pageSize = 10,
        string? searchQuery = null,
        long? roleId = null,
        long? outletId = null,
        bool? isActive = null)
    {
        var query = _context.Users
            .Include(u => u.Role)
            .Include(u => u.Outlet)
            .AsQueryable();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            query = query.Where(u =>
                u.Name.Contains(searchQuery) ||
                u.Email.Contains(searchQuery));
        }

        if (roleId.HasValue)
        {
            query = query.Where(u => u.RoleId == roleId.Value);
        }

        if (outletId.HasValue)
        {
            query = query.Where(u => u.OutletId == outletId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        // Apply pagination
        return await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetTotalCountAsync(
        string? searchQuery = null,
        long? roleId = null,
        long? outletId = null,
        bool? isActive = null)
    {
        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            query = query.Where(u =>
                u.Name.Contains(searchQuery) ||
                u.Email.Contains(searchQuery));
        }

        if (roleId.HasValue)
        {
            query = query.Where(u => u.RoleId == roleId.Value);
        }

        if (outletId.HasValue)
        {
            query = query.Where(u => u.OutletId == outletId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        return await query.CountAsync();
    }

    public async Task<User> CreateAsync(User user)
    {
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        
        // Reload with navigation properties
        return (await GetByIdAsync(user.Id))!;
    }

    public async Task<User> UpdateAsync(User user)
    {
        user.UpdatedAt = DateTime.UtcNow;
        
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        
        // Reload with navigation properties
        return (await GetByIdAsync(user.Id))!;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            return false;
        }

        // Soft delete - set IsActive to false
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(string email)
    {
        return await _context.Users.AnyAsync(u => u.Email == email);
    }

    public async Task<IEnumerable<User>> GetByOutletIdAsync(long outletId)
    {
        return await _context.Users
            .Include(u => u.Role)
            .Include(u => u.Outlet)
            .Where(u => u.OutletId == outletId && u.IsActive)
            .OrderBy(u => u.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<User>> GetByRoleIdAsync(long roleId)
    {
        return await _context.Users
            .Include(u => u.Role)
            .Include(u => u.Outlet)
            .Where(u => u.RoleId == roleId && u.IsActive)
            .OrderBy(u => u.Name)
            .ToListAsync();
    }
}
