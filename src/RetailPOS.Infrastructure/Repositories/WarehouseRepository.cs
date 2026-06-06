using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class WarehouseRepository : IWarehouseRepository
{
    private readonly RetailPOSDbContext _context;

    public WarehouseRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Warehouse>> GetAllAsync(long? businessId = null)
    {
        var query = _context.Warehouses
            .Include(w => w.Manager)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(w => w.BusinessId == businessId.Value);

        return await query
            .OrderBy(w => w.Name)
            .ToListAsync();
    }

    public async Task<Warehouse?> GetByIdAsync(long id, long? businessId = null)
    {
        var query = _context.Warehouses
            .Include(w => w.Manager)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(w => w.BusinessId == businessId.Value);

        return await query.FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task<Warehouse?> GetByIdWithDetailsAsync(long id, long? businessId = null)
    {
        var query = _context.Warehouses
            .Include(w => w.Manager)
            .Include(w => w.PurchaseOrders)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(w => w.BusinessId == businessId.Value);

        return await query.FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task<Warehouse> CreateAsync(Warehouse warehouse)
    {
        warehouse.CreatedAt = DateTime.UtcNow;
        warehouse.UpdatedAt = DateTime.UtcNow;
        
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();
        
        return await GetByIdAsync(warehouse.Id, warehouse.BusinessId) ?? warehouse;
    }

    public async Task<Warehouse> UpdateAsync(Warehouse warehouse)
    {
        warehouse.UpdatedAt = DateTime.UtcNow;
        
        _context.Warehouses.Update(warehouse);
        await _context.SaveChangesAsync();
        
        return await GetByIdAsync(warehouse.Id, warehouse.BusinessId) ?? warehouse;
    }

    public async Task<bool> DeleteAsync(long id, long? businessId = null)
    {
        var query = _context.Warehouses.AsQueryable();
        if (businessId.HasValue)
            query = query.Where(w => w.BusinessId == businessId.Value);

        var warehouse = await query.FirstOrDefaultAsync(w => w.Id == id);
        if (warehouse == null)
            return false;

        _context.Warehouses.Remove(warehouse);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(long id, long? businessId = null)
    {
        var query = _context.Warehouses.AsQueryable();
        if (businessId.HasValue)
            query = query.Where(w => w.BusinessId == businessId.Value);
        return await query.AnyAsync(w => w.Id == id);
    }

    public async Task<bool> ExistsByNameAsync(string name, long? excludeId = null, long? businessId = null)
    {
        var query = _context.Warehouses.Where(w => w.Name.ToLower() == name.ToLower());

        if (businessId.HasValue)
            query = query.Where(w => w.BusinessId == businessId.Value);
        
        if (excludeId.HasValue)
            query = query.Where(w => w.Id != excludeId.Value);
        
        return await query.AnyAsync();
    }

    public async Task<IEnumerable<Warehouse>> SearchAsync(string searchTerm, long? businessId = null)
    {
        var query = _context.Warehouses
            .Include(w => w.Manager)
            .Where(w => w.Name.Contains(searchTerm) || 
                       w.Address.Contains(searchTerm))
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(w => w.BusinessId == businessId.Value);

        return await query
            .OrderBy(w => w.Name)
            .ToListAsync();
    }

    public async Task<int> GetPurchaseOrderCountAsync(long warehouseId, long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Where(po => po.WarehouseId == warehouseId)
            .AsQueryable();

        if (businessId.HasValue)
        {
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);
        }

        return await query
            .CountAsync();
    }
}
