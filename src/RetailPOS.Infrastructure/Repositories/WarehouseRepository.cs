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

    public async Task<IEnumerable<Warehouse>> GetAllAsync()
    {
        return await _context.Warehouses
            .Include(w => w.Manager)
            .OrderBy(w => w.Name)
            .ToListAsync();
    }

    public async Task<Warehouse?> GetByIdAsync(long id)
    {
        return await _context.Warehouses
            .Include(w => w.Manager)
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task<Warehouse?> GetByIdWithDetailsAsync(long id)
    {
        return await _context.Warehouses
            .Include(w => w.Manager)
            .Include(w => w.PurchaseOrders)
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task<Warehouse> CreateAsync(Warehouse warehouse)
    {
        warehouse.CreatedAt = DateTime.UtcNow;
        warehouse.UpdatedAt = DateTime.UtcNow;
        
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();
        
        return await GetByIdAsync(warehouse.Id) ?? warehouse;
    }

    public async Task<Warehouse> UpdateAsync(Warehouse warehouse)
    {
        warehouse.UpdatedAt = DateTime.UtcNow;
        
        _context.Warehouses.Update(warehouse);
        await _context.SaveChangesAsync();
        
        return await GetByIdAsync(warehouse.Id) ?? warehouse;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var warehouse = await _context.Warehouses.FindAsync(id);
        if (warehouse == null)
            return false;

        _context.Warehouses.Remove(warehouse);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(long id)
    {
        return await _context.Warehouses.AnyAsync(w => w.Id == id);
    }

    public async Task<bool> ExistsByNameAsync(string name, long? excludeId = null)
    {
        var query = _context.Warehouses.Where(w => w.Name.ToLower() == name.ToLower());
        
        if (excludeId.HasValue)
            query = query.Where(w => w.Id != excludeId.Value);
        
        return await query.AnyAsync();
    }

    public async Task<IEnumerable<Warehouse>> SearchAsync(string searchTerm)
    {
        return await _context.Warehouses
            .Include(w => w.Manager)
            .Where(w => w.Name.Contains(searchTerm) || 
                       w.Address.Contains(searchTerm))
            .OrderBy(w => w.Name)
            .ToListAsync();
    }

    public async Task<int> GetPurchaseOrderCountAsync(long warehouseId)
    {
        return await _context.PurchaseOrders
            .Where(po => po.WarehouseId == warehouseId)
            .CountAsync();
    }
}
