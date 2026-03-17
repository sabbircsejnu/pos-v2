using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

/// <summary>
/// Provides aggregated inventory report data directly from the database
/// </summary>
public class InventoryReportService : IInventoryReportService
{
    private readonly RetailPOSDbContext _context;

    public InventoryReportService(RetailPOSDbContext context)
    {
        _context = context;
    }

    /// <summary>Returns stock levels with optional location and low-stock filters</summary>
    public async Task<List<StockLevelDto>> GetStockLevelsAsync(long? locationId, string? locationType, bool lowStockOnly)
    {
        var query = _context.Inventories
            .AsNoTracking()
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .AsQueryable();

        if (locationId.HasValue)
            query = query.Where(i => i.LocationId == locationId.Value);

        if (!string.IsNullOrWhiteSpace(locationType))
            query = query.Where(i => i.LocationType == locationType);

        if (lowStockOnly)
            query = query.Where(i => i.Quantity <= i.LowStockThreshold);

        var inventories = await query.ToListAsync();

        // Resolve location names in memory (outlets + warehouses keyed by id)
        var outletIds = inventories.Where(i => i.LocationType == "outlet").Select(i => i.LocationId).Distinct().ToList();
        var warehouseIds = inventories.Where(i => i.LocationType == "warehouse").Select(i => i.LocationId).Distinct().ToList();

        var outletNames = await _context.Outlets.AsNoTracking().Where(o => outletIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name);
        var warehouseNames = await _context.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name);

        return inventories.Select(i =>
        {
            var locationName = i.LocationType == "outlet"
                ? (outletNames.TryGetValue(i.LocationId, out var n1) ? n1 : string.Empty)
                : (warehouseNames.TryGetValue(i.LocationId, out var n2) ? n2 : string.Empty);

            var costPrice = i.Variant.Product.CostPrice + i.Variant.CostAdjustment;

            return new StockLevelDto
            {
                VariantId      = i.VariantId,
                ProductName    = i.Variant.Product.Name,
                Sku            = i.Variant.Sku,
                CategoryName   = i.Variant.Product.Category?.Name ?? "Uncategorized",
                LocationId     = i.LocationId,
                LocationType   = i.LocationType,
                LocationName   = locationName,
                Quantity       = i.Quantity,
                ReorderLevel   = i.LowStockThreshold,
                IsLowStock     = i.Quantity <= i.LowStockThreshold,
                EstimatedValue = costPrice * i.Quantity
            };
        }).ToList();
    }

    /// <summary>Returns total inventory value with per-category breakdown</summary>
    public async Task<InventoryValuationDto> GetInventoryValuationAsync()
    {
        var inventories = await _context.Inventories
            .AsNoTracking()
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .ToListAsync();

        var byCategory = inventories
            .GroupBy(i => i.Variant.Product.Category?.Name ?? "Uncategorized")
            .Select(g => new CategoryValuationDto
            {
                CategoryName = g.Key,
                Value        = g.Sum(i => (i.Variant.Product.CostPrice + i.Variant.CostAdjustment) * i.Quantity),
                ItemCount    = g.Sum(i => i.Quantity)
            })
            .OrderByDescending(c => c.Value)
            .ToList();

        return new InventoryValuationDto
        {
            TotalValue  = byCategory.Sum(c => c.Value),
            TotalItems  = byCategory.Sum(c => c.ItemCount),
            ByCategory  = byCategory
        };
    }

    /// <summary>Returns variants with no sales activity within the specified number of days</summary>
    public async Task<List<SlowMovingItemDto>> GetSlowMovingItemsAsync(int days = 90)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);

        // Variant IDs that had a sale since the cutoff
        var activeSaleVariantIds = await _context.SaleItems
            .AsNoTracking()
            .Where(si => si.Sale.SaleDate >= cutoff)
            .Select(si => si.VariantId)
            .Distinct()
            .ToListAsync();

        var inventories = await _context.Inventories
            .AsNoTracking()
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
            .Where(i => !activeSaleVariantIds.Contains(i.VariantId))
            .ToListAsync();

        // Find the last sale date per variant for DaysSinceLastSale
        var variantIds = inventories.Select(i => i.VariantId).Distinct().ToList();
        var lastSaleDates = await _context.SaleItems
            .AsNoTracking()
            .Where(si => variantIds.Contains(si.VariantId))
            .GroupBy(si => si.VariantId)
            .Select(g => new { VariantId = g.Key, LastSale = g.Max(si => si.Sale.SaleDate) })
            .ToDictionaryAsync(x => x.VariantId, x => x.LastSale);

        var now = DateTime.UtcNow;
        return inventories
            .GroupBy(i => i.VariantId)
            .Select(g =>
            {
                var first      = g.First();
                var costPrice  = first.Variant.Product.CostPrice + first.Variant.CostAdjustment;
                var totalStock = g.Sum(i => i.Quantity);
                var daysSince  = lastSaleDates.TryGetValue(first.VariantId, out var last)
                    ? (int)(now - last).TotalDays
                    : int.MaxValue; // never sold

                return new SlowMovingItemDto
                {
                    VariantId          = first.VariantId,
                    ProductName        = first.Variant.Product.Name,
                    Sku                = first.Variant.Sku,
                    CurrentStock       = totalStock,
                    DaysSinceLastSale  = daysSince,
                    EstimatedValue     = costPrice * totalStock
                };
            })
            .OrderByDescending(x => x.DaysSinceLastSale)
            .ToList();
    }
}
