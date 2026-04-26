using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class OutletPriceOverrideRepository : IOutletPriceOverrideRepository
{
    private readonly RetailPOSDbContext _context;

    public OutletPriceOverrideRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<OutletPriceOverride?> GetByIdAsync(long id)
        => await _context.OutletPriceOverrides
            .Include(o => o.Outlet)
            .Include(o => o.ProductVariant).ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

    public async Task<OutletPriceOverride?> GetActiveForVariantAsync(long outletId, long variantId)
        => await _context.OutletPriceOverrides
            .FirstOrDefaultAsync(o =>
                o.OutletId == outletId &&
                o.ProductVariantId == variantId &&
                o.IsActive);

    public async Task<IEnumerable<OutletPriceOverride>> GetByOutletAsync(long outletId)
        => await _context.OutletPriceOverrides
            .Include(o => o.ProductVariant).ThenInclude(v => v.Product)
            .Where(o => o.OutletId == outletId)
            .OrderBy(o => o.ProductVariant.Product.Name)
            .ThenBy(o => o.ProductVariant.Name)
            .ToListAsync();

    public async Task<IEnumerable<OutletPriceOverride>> GetByVariantAsync(long variantId)
        => await _context.OutletPriceOverrides
            .Include(o => o.Outlet)
            .Where(o => o.ProductVariantId == variantId)
            .OrderBy(o => o.Outlet.Name)
            .ToListAsync();

    public async Task<OutletPriceOverride> CreateAsync(OutletPriceOverride entity)
    {
        _context.OutletPriceOverrides.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<OutletPriceOverride> UpdateAsync(OutletPriceOverride entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        _context.OutletPriceOverrides.Update(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _context.OutletPriceOverrides.FindAsync(id);
        if (entity == null) return false;
        _context.OutletPriceOverrides.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(long outletId, long variantId, long? excludeId = null)
        => await _context.OutletPriceOverrides
            .AnyAsync(o =>
                o.OutletId == outletId &&
                o.ProductVariantId == variantId &&
                o.IsActive &&
                (excludeId == null || o.Id != excludeId));
}
