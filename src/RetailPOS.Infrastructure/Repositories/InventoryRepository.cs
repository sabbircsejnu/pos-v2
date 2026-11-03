using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly RetailPOSDbContext _context;

    public InventoryRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Inventory?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<List<Inventory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Inventory>> GetByOutletAsync(long outletId, CancellationToken cancellationToken = default)
    {
        return await _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.LocationType.ToLower() == "outlet" && i.LocationId == outletId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Inventory>> GetByWarehouseAsync(long warehouseId, CancellationToken cancellationToken = default)
    {
        return await _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.LocationType.ToLower() == "warehouse" && i.LocationId == warehouseId)
            .ToListAsync(cancellationToken);
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

    public async Task<List<Inventory>> GetLowStockAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.Quantity <= i.LowStockThreshold);

        query = ApplyLocationScope(query, locationScope);

        return await query.OrderBy(i => i.Quantity).ToListAsync(cancellationToken);
    }

    public async Task<List<Inventory>> GetOutOfStockAsync(
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.Quantity == 0);

        query = ApplyLocationScope(query, locationScope);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<Inventory>> GetExpiringSoonAsync(
        int days,
        InventoryLocationScope locationScope,
        CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow.AddDays(days);
        
        var query = _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.ExpiryDate.HasValue && 
                       i.ExpiryDate.Value <= threshold && 
                       i.Quantity > 0);

        query = ApplyLocationScope(query, locationScope);

        return await query.OrderBy(i => i.ExpiryDate).ToListAsync(cancellationToken);
    }

    public async Task<List<Inventory>> SearchAsync(
        string? productSearch,
        long? variantId,
        string? variantSearch,
        long? categoryId,
        InventoryLocationScope locationScope,
        bool? lowStockOnly = null,
        bool? outOfStockOnly = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Inventories
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(productSearch))
        {
            var search = productSearch.Trim();
            var likePattern = $"%{search}%";

            query = query.Where(i =>
                EF.Functions.ILike(i.Variant.Product.Name, likePattern) ||
                (i.Variant.Product.ProductCode != null && EF.Functions.ILike(i.Variant.Product.ProductCode, likePattern))
            );
        }

        if (variantId.HasValue)
            query = query.Where(i => i.VariantId == variantId.Value);

        if (!string.IsNullOrWhiteSpace(variantSearch))
        {
            var search = variantSearch.Trim();
            var likePattern = $"%{search}%";

            // Attributes is stored as jsonb in PostgreSQL; cast to text for case-insensitive matching.
            var attributeMatchingVariantIds = await _context.Database
                .SqlQueryRaw<long>(
                    "SELECT id FROM product_variants WHERE attributes::text ILIKE {0}",
                    likePattern)
                .ToListAsync(cancellationToken);

            query = query.Where(i =>
                EF.Functions.ILike(i.Variant.Name, likePattern) ||
                (i.Variant.Sku != null && EF.Functions.ILike(i.Variant.Sku, likePattern)) ||
                (i.Variant.Barcode != null && EF.Functions.ILike(i.Variant.Barcode, likePattern)) ||
                attributeMatchingVariantIds.Contains(i.VariantId)
            );
        }

        if (categoryId.HasValue)
            query = query.Where(i => i.Variant.Product.CategoryId == categoryId);

        query = ApplyLocationScope(query, locationScope);

        if (lowStockOnly == true)
            query = query.Where(i => i.Quantity <= i.LowStockThreshold && i.Quantity > 0);

        if (outOfStockOnly == true)
            query = query.Where(i => i.Quantity == 0);

        return await query.ToListAsync(cancellationToken);
    }

    private static IQueryable<Inventory> ApplyLocationScope(
        IQueryable<Inventory> query,
        InventoryLocationScope locationScope) =>
        query.Where(inventory =>
            (inventory.LocationType.ToLower() == "outlet" &&
             locationScope.OutletIds.Contains(inventory.LocationId)) ||
            (inventory.LocationType.ToLower() == "warehouse" &&
             locationScope.WarehouseIds.Contains(inventory.LocationId)));

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

    public async Task<Inventory> UpdateAsync(
        Inventory inventory,
        CancellationToken cancellationToken = default)
    {
        _context.Inventories.Update(inventory);
        await _context.SaveChangesAsync(cancellationToken);
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

    public async Task<int> GetTotalQuantityByVariantAsync(
        long variantId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Inventories
            .Where(i => i.VariantId == variantId)
            .SumAsync(i => i.Quantity, cancellationToken);
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
