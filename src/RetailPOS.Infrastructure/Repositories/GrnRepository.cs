using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.PurchaseOrders;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for GRN (Goods Received Note) data access operations
/// </summary>
public class GrnRepository : IGrnRepository
{
    private readonly RetailPOSDbContext _context;

    public GrnRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Grn?> GetByIdAsync(long id)
    {
        return await _context.Grns
            .Include(g => g.PurchaseOrder)
                .ThenInclude(po => po.Supplier)
            .Include(g => g.PurchaseOrder)
                .ThenInclude(po => po.Warehouse)
            .Include(g => g.PurchaseOrder)
                .ThenInclude(po => po.Outlet)
            .Include(g => g.PurchaseOrder)
                .ThenInclude(po => po.Creator)
            .Include(g => g.Creator)
            .Include(g => g.Items)
                .ThenInclude(i => i.PurchaseOrderItem)
                    .ThenInclude(poi => poi.Variant)
                        .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<Grn> UpdateAsync(Grn entity)
    {
        _context.Grns.Update(entity);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(entity.Id))!;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var grn = await _context.Grns
            .Include(g => g.Items)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (grn == null)
            return false;

        _context.GrnItems.RemoveRange(grn.Items);
        _context.Grns.Remove(grn);
        await _context.SaveChangesAsync();
        return true;
    }

}
