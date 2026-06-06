using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class OutletRepository : IOutletRepository
{
    private readonly RetailPOSDbContext _context;

    public OutletRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Outlet>> GetAllAsync(long? businessId = null)
    {
        var query = _context.Outlets
            .Include(o => o.Manager)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(o => o.BusinessId == businessId.Value);

        return await query
            .OrderBy(o => o.Name)
            .ToListAsync();
    }

    public async Task<Outlet?> GetByIdAsync(long id, long? businessId = null)
    {
        var query = _context.Outlets
            .Include(o => o.Manager)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(o => o.BusinessId == businessId.Value);

        return await query.FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Outlet?> GetByIdWithDetailsAsync(long id, long? businessId = null)
    {
        var query = _context.Outlets
            .Include(o => o.Manager)
            .Include(o => o.Users)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(o => o.BusinessId == businessId.Value);

        return await query.FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Outlet> CreateAsync(Outlet outlet)
    {
        outlet.CreatedAt = DateTime.UtcNow;
        outlet.UpdatedAt = DateTime.UtcNow;
        
        _context.Outlets.Add(outlet);
        await _context.SaveChangesAsync();
        
        return await GetByIdAsync(outlet.Id, outlet.BusinessId) ?? outlet;
    }

    public async Task<Outlet> UpdateAsync(Outlet outlet)
    {
        outlet.UpdatedAt = DateTime.UtcNow;
        
        _context.Outlets.Update(outlet);
        await _context.SaveChangesAsync();
        
        return await GetByIdAsync(outlet.Id, outlet.BusinessId) ?? outlet;
    }

    public async Task<bool> DeleteAsync(long id, long? businessId = null)
    {
        var query = _context.Outlets.AsQueryable();
        if (businessId.HasValue)
            query = query.Where(o => o.BusinessId == businessId.Value);

        var outlet = await query.FirstOrDefaultAsync(o => o.Id == id);
        if (outlet == null)
            return false;

        _context.Outlets.Remove(outlet);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(long id, long? businessId = null)
    {
        var query = _context.Outlets.AsQueryable();
        if (businessId.HasValue)
            query = query.Where(o => o.BusinessId == businessId.Value);
        return await query.AnyAsync(o => o.Id == id);
    }

    public async Task<bool> ExistsByNameAsync(string name, long? excludeId = null, long? businessId = null)
    {
        var query = _context.Outlets.Where(o => o.Name.ToLower() == name.ToLower());

        if (businessId.HasValue)
            query = query.Where(o => o.BusinessId == businessId.Value);
        
        if (excludeId.HasValue)
            query = query.Where(o => o.Id != excludeId.Value);
        
        return await query.AnyAsync();
    }

    public async Task<IEnumerable<Outlet>> SearchAsync(string searchTerm, long? businessId = null)
    {
        var query = _context.Outlets
            .Include(o => o.Manager)
            .Where(o => o.Name.Contains(searchTerm) || 
                       o.Address.Contains(searchTerm) ||
                       (o.ContactNumber != null && o.ContactNumber.Contains(searchTerm)))
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(o => o.BusinessId == businessId.Value);

        return await query
            .OrderBy(o => o.Name)
            .ToListAsync();
    }

    public async Task<int> GetUserCountAsync(long outletId, long? businessId = null)
    {
        var query = _context.Users
            .Where(u => u.OutletId == outletId && u.IsActive)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(u => u.BusinessId == businessId.Value);

        return await query
            .CountAsync();
    }

    public async Task<IEnumerable<User>> GetOutletUsersAsync(long outletId, long? businessId = null)
    {
        var query = _context.Users
            .Include(u => u.Role)
            .Where(u => u.OutletId == outletId && u.IsActive)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(u => u.BusinessId == businessId.Value);

        return await query
            .OrderBy(u => u.Name)
            .ToListAsync();
    }
}
