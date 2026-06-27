using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly RetailPOSDbContext _context;

    public InventoryRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Inventory?> GetByIdAsync(long id)
    {
        return await _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<List<Inventory>> GetAllAsync()
    {
        return await _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .ToListAsync();
    }

    public async Task<List<Inventory>> GetByOutletAsync(long outletId)
    {
        return await _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.LocationType.ToLower() == "outlet" && i.LocationId == outletId)
            .ToListAsync();
    }

    public async Task<List<Inventory>> GetByWarehouseAsync(long warehouseId)
    {
        return await _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.LocationType.ToLower() == "warehouse" && i.LocationId == warehouseId)
            .ToListAsync();
    }

    public async Task<Inventory?> GetByVariantAndLocationAsync(long variantId, long locationId, string locationType)
    {
        var normalizedType = NormalizeLocationType(locationType);

        return await _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(i => i.VariantId == variantId && 
                                    i.LocationId == locationId && 
                                    i.LocationType.ToLower() == normalizedType);
    }

    public async Task<List<Inventory>> GetLowStockAsync(long? outletId = null, long? warehouseId = null)
    {
        var query = _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.Quantity <= i.LowStockThreshold);

        if (outletId.HasValue)
            query = query.Where(i => i.LocationType.ToLower() == "outlet" && i.LocationId == outletId);
        else if (warehouseId.HasValue)
            query = query.Where(i => i.LocationType.ToLower() == "warehouse" && i.LocationId == warehouseId);

        return await query.OrderBy(i => i.Quantity).ToListAsync();
    }

    public async Task<List<Inventory>> GetOutOfStockAsync(long? outletId = null, long? warehouseId = null)
    {
        var query = _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.Quantity == 0);

        if (outletId.HasValue)
            query = query.Where(i => i.LocationType.ToLower() == "outlet" && i.LocationId == outletId);
        else if (warehouseId.HasValue)
            query = query.Where(i => i.LocationType.ToLower() == "warehouse" && i.LocationId == warehouseId);

        return await query.ToListAsync();
    }

    public async Task<List<Inventory>> GetExpiringSoonAsync(int days = 30, long? outletId = null, long? warehouseId = null)
    {
        var threshold = DateTime.UtcNow.AddDays(days);
        
        var query = _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.ExpiryDate.HasValue && 
                       i.ExpiryDate.Value <= threshold && 
                       i.Quantity > 0);

        if (outletId.HasValue)
            query = query.Where(i => i.LocationType.ToLower() == "outlet" && i.LocationId == outletId);
        else if (warehouseId.HasValue)
            query = query.Where(i => i.LocationType.ToLower() == "warehouse" && i.LocationId == warehouseId);

        return await query.OrderBy(i => i.ExpiryDate).ToListAsync();
    }

    public async Task<List<Inventory>> SearchAsync(
        string? productSearch = null,
        long? variantId = null,
        string? variantSearch = null,
        long? categoryId = null,
        long? outletId = null,
        long? warehouseId = null,
        bool? lowStockOnly = null,
        bool? outOfStockOnly = null)
    {
        var query = _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(productSearch))
        {
            var search = productSearch.Trim().ToLower();
            query = query.Where(i =>
                i.Variant.Product.Name.ToLower().Contains(search) ||
                (i.Variant.Product.ProductCode != null && i.Variant.Product.ProductCode.ToLower().Contains(search))
            );
        }

        if (variantId.HasValue)
            query = query.Where(i => i.VariantId == variantId.Value);

        if (!string.IsNullOrWhiteSpace(variantSearch))
        {
            var search = variantSearch.Trim().ToLower();
            query = query.Where(i =>
                i.Variant.Name.ToLower().Contains(search) ||
                (i.Variant.Attributes != null && i.Variant.Attributes.ToLower().Contains(search)) ||
                (i.Variant.Sku != null && i.Variant.Sku.ToLower().Contains(search)) ||
                (i.Variant.Barcode != null && i.Variant.Barcode.ToLower().Contains(search))
            );
        }

        if (categoryId.HasValue)
            query = query.Where(i => i.Variant.Product.CategoryId == categoryId);

        if (outletId.HasValue)
            query = query.Where(i => i.LocationType.ToLower() == "outlet" && i.LocationId == outletId);

        if (warehouseId.HasValue)
            query = query.Where(i => i.LocationType.ToLower() == "warehouse" && i.LocationId == warehouseId);

        if (lowStockOnly == true)
            query = query.Where(i => i.Quantity <= i.LowStockThreshold && i.Quantity > 0);

        if (outOfStockOnly == true)
            query = query.Where(i => i.Quantity == 0);

        return await query.ToListAsync();
    }

    public async Task<bool> ExistsAsync(long variantId, long locationId, string locationType)
    {
        var normalizedType = NormalizeLocationType(locationType);

        return await _context.Inventories
            .AnyAsync(i => i.VariantId == variantId && 
                         i.LocationId == locationId && 
                         i.LocationType.ToLower() == normalizedType);
    }

    public async Task<Inventory> CreateAsync(Inventory inventory)
    {
        _context.Inventories.Add(inventory);
        await _context.SaveChangesAsync();
        return inventory;
    }

    public async Task<Inventory> UpdateAsync(Inventory inventory)
    {
        _context.Inventories.Update(inventory);
        await _context.SaveChangesAsync();
        return inventory;
    }

    public async Task DeleteAsync(long id)
    {
        var inventory = await _context.Inventories.FindAsync(id);
        if (inventory != null)
        {
            _context.Inventories.Remove(inventory);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<int> GetTotalQuantityByVariantAsync(long variantId)
    {
        return await _context.Inventories
            .Where(i => i.VariantId == variantId)
            .SumAsync(i => i.Quantity);
    }

    private static string NormalizeLocationType(string locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
            throw new InvalidOperationException("Location type is required.");

        var normalized = locationType.Trim().ToLowerInvariant();
        if (normalized != "outlet" && normalized != "warehouse")
            throw new InvalidOperationException($"Invalid location type '{locationType}'.");

        return normalized;
    }
}
