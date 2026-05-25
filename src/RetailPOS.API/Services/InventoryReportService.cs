using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.Core.Entities;
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

    /// <summary>
    /// Builds a stock-card style report from the unified StockLedger:
    /// - Opening balance is computed from rows strictly before <paramref name="dateFrom"/>.
    /// - In-window rows are returned chronologically with running balance.
    /// - Closing = Opening + StockIn - StockOut for the window.
    /// </summary>
    public async Task<StockTransactionReportDto> GetStockTransactionReportAsync(
        long productId,
        long? variantId,
        long? locationId,
        string? locationType,
        DateTime? dateFrom,
        DateTime? dateTo)
    {
        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null)
            throw new KeyNotFoundException($"Product {productId} not found");

        // Variant IDs in scope: a single variant or every variant of the product.
        var variantIdsInScope = variantId.HasValue
            ? new List<long> { variantId.Value }
            : await _context.ProductVariants
                .AsNoTracking()
                .Where(v => v.ProductId == productId)
                .Select(v => v.Id)
                .ToListAsync();

        ProductVariant? singleVariant = null;
        if (variantId.HasValue)
        {
            singleVariant = await _context.ProductVariants
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == variantId.Value && v.ProductId == productId);

            if (singleVariant == null)
                throw new KeyNotFoundException($"Variant {variantId.Value} does not belong to product {productId}");
        }

        // Inclusive end-of-day for the supplied DateTo
        var rangeStart = dateFrom;
        var rangeEnd = dateTo.HasValue
            ? dateTo.Value.Date.AddDays(1).AddTicks(-1)
            : (DateTime?)null;

        var baseQuery = _context.StockLedgers
            .AsNoTracking()
            .Where(sl => variantIdsInScope.Contains(sl.VariantId));

        if (locationId.HasValue)
            baseQuery = baseQuery.Where(sl => sl.LocationId == locationId.Value);

        if (!string.IsNullOrWhiteSpace(locationType))
            baseQuery = baseQuery.Where(sl => sl.LocationType == locationType);

        // Opening balance = NET movement strictly before the window starts.
        int opening = 0;
        if (rangeStart.HasValue)
        {
            opening = await baseQuery
                .Where(sl => sl.CreatedAt < rangeStart.Value)
                .SumAsync(sl => (int?)(sl.QtyIn - sl.QtyOut)) ?? 0;
        }

        var inWindowQuery = baseQuery;
        if (rangeStart.HasValue)
            inWindowQuery = inWindowQuery.Where(sl => sl.CreatedAt >= rangeStart.Value);
        if (rangeEnd.HasValue)
            inWindowQuery = inWindowQuery.Where(sl => sl.CreatedAt <= rangeEnd.Value);

        var ledgerRows = await inWindowQuery
            .OrderBy(sl => sl.CreatedAt)
            .ThenBy(sl => sl.Id)
            .Select(sl => new
            {
                sl.Id,
                sl.CreatedAt,
                sl.LocationId,
                sl.LocationType,
                sl.TransactionType,
                sl.ReferenceType,
                sl.ReferenceId,
                sl.VariantId,
                VariantSku = sl.Variant.Sku,
                VariantAttributes = sl.Variant.Attributes,
                sl.QtyIn,
                sl.QtyOut,
                sl.Remarks,
                CreatedByName = sl.Creator != null ? sl.Creator.Name : null
            })
            .ToListAsync();

        // Resolve location, sale, grn, transfer reference numbers up front (one round-trip each)
        var outletIds = ledgerRows.Where(r => r.LocationType == "outlet").Select(r => r.LocationId).Distinct().ToList();
        var warehouseIds = ledgerRows.Where(r => r.LocationType == "warehouse").Select(r => r.LocationId).Distinct().ToList();

        var outletNames = await _context.Outlets.AsNoTracking()
            .Where(o => outletIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name);

        var warehouseNames = await _context.Warehouses.AsNoTracking()
            .Where(w => warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name);

        var saleIds = ledgerRows.Where(r => r.ReferenceType == StockLedgerReferenceType.Sale).Select(r => r.ReferenceId).Distinct().ToList();
        var grnIds = ledgerRows.Where(r => r.ReferenceType == StockLedgerReferenceType.Grn).Select(r => r.ReferenceId).Distinct().ToList();
        var transferIds = ledgerRows.Where(r => r.ReferenceType == StockLedgerReferenceType.StockTransfer).Select(r => r.ReferenceId).Distinct().ToList();
        var adjustmentIds = ledgerRows.Where(r => r.ReferenceType == StockLedgerReferenceType.StockAdjustment).Select(r => r.ReferenceId).Distinct().ToList();

        var saleNumbers = await _context.Sales.AsNoTracking()
            .Where(s => saleIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.SaleNumber);

        var grnNumbers = await _context.Grns.AsNoTracking()
            .Where(g => grnIds.Contains(g.Id))
            .Select(g => new { g.Id, g.PoId })
            .ToDictionaryAsync(g => g.Id, g => $"GRN-{g.Id} (PO-{g.PoId})");

        var transferNumbers = transferIds.ToDictionary(id => id, id => $"TRF-{id}");
        var adjustmentNumbers = adjustmentIds.ToDictionary(id => id, id => $"ADJ-{id}");

        // Walk chronologically, accumulating running balance from opening.
        int running = opening;
        int totalIn = 0;
        int totalOut = 0;
        var rows = new List<StockTransactionReportRowDto>(ledgerRows.Count);

        foreach (var r in ledgerRows)
        {
            running += r.QtyIn - r.QtyOut;
            totalIn += r.QtyIn;
            totalOut += r.QtyOut;

            var locationName = r.LocationType == "outlet"
                ? (outletNames.TryGetValue(r.LocationId, out var n1) ? n1 : $"Outlet {r.LocationId}")
                : r.LocationType == "warehouse"
                    ? (warehouseNames.TryGetValue(r.LocationId, out var n2) ? n2 : $"Warehouse {r.LocationId}")
                    : $"Location {r.LocationId}";

            var referenceNo = r.ReferenceType switch
            {
                StockLedgerReferenceType.Sale            => saleNumbers.TryGetValue(r.ReferenceId, out var sn) ? sn : $"SALE-{r.ReferenceId}",
                StockLedgerReferenceType.Grn             => grnNumbers.TryGetValue(r.ReferenceId, out var gn) ? gn : $"GRN-{r.ReferenceId}",
                StockLedgerReferenceType.StockTransfer   => transferNumbers.TryGetValue(r.ReferenceId, out var tn) ? tn : $"TRF-{r.ReferenceId}",
                StockLedgerReferenceType.StockAdjustment => adjustmentNumbers.TryGetValue(r.ReferenceId, out var an) ? an : $"ADJ-{r.ReferenceId}",
                _ => $"{r.ReferenceType}-{r.ReferenceId}"
            };

            rows.Add(new StockTransactionReportRowDto
            {
                Id                = r.Id,
                TransactionDate   = r.CreatedAt,
                LocationId        = r.LocationId,
                LocationType      = r.LocationType,
                LocationName      = locationName,
                TransactionType   = r.TransactionType,
                ReferenceType     = r.ReferenceType,
                ReferenceId       = r.ReferenceId,
                ReferenceNo       = referenceNo,
                VariantId         = r.VariantId,
                VariantCode       = r.VariantSku,
                VariantAttributes = r.VariantAttributes,
                QuantityIn        = r.QtyIn,
                QuantityOut       = r.QtyOut,
                RunningBalance    = running,
                Remarks           = r.Remarks,
                CreatedByName     = r.CreatedByName
            });
        }

        // Current stock across the same scope (for validation).
        var currentStockQuery = _context.Inventories.AsNoTracking()
            .Where(i => variantIdsInScope.Contains(i.VariantId));
        if (locationId.HasValue)
            currentStockQuery = currentStockQuery.Where(i => i.LocationId == locationId.Value);
        if (!string.IsNullOrWhiteSpace(locationType))
            currentStockQuery = currentStockQuery.Where(i => i.LocationType == locationType);

        var currentStock = await currentStockQuery.SumAsync(i => (int?)i.Quantity) ?? 0;

        var displaySku = singleVariant?.Sku ?? product.Sku ?? string.Empty;

        return new StockTransactionReportDto
        {
            ProductId         = product.Id,
            ProductName       = product.Name,
            ProductCode       = product.Sku,
            Level             = singleVariant != null ? "variant" : "product",
            VariantId         = singleVariant?.Id,
            VariantCode       = singleVariant?.Sku,
            VariantName       = singleVariant?.Name,
            VariantAttributes = singleVariant?.Attributes,
            Sku               = displaySku,
            OutletId          = locationId,
            LocationType      = locationType,
            DateFrom          = dateFrom,
            DateTo            = dateTo,
            Summary = new StockTransactionReportSummaryDto
            {
                OpeningStock = opening,
                StockIn      = totalIn,
                StockOut     = totalOut,
                ClosingStock = opening + totalIn - totalOut,
                CurrentStock = currentStock
            },
            Rows = rows
        };
    }
}
