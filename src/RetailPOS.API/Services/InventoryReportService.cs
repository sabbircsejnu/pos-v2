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
            ProductCode       = product.ProductCode ?? string.Empty,
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

    // ─────────────────────────────────────────────────────────────────────
    // Current Stock Report
    // ─────────────────────────────────────────────────────────────────────

    public async Task<CurrentStockReportDto> GetCurrentStockReportAsync(CurrentStockFilterDto filter)
    {
        var (rows, summary) = await BuildCurrentStockAsync(filter, paginate: true);
        return new CurrentStockReportDto
        {
            Items      = rows,
            TotalCount = summary.TotalProducts,
            Summary    = summary
        };
    }

    public async Task<List<CurrentStockRowDto>> GetCurrentStockExportAsync(CurrentStockFilterDto filter)
    {
        var (rows, _) = await BuildCurrentStockAsync(filter, paginate: false);
        return rows;
    }

    /// <summary>
    /// Shared query builder for Current Stock Report.
    /// When <paramref name="paginate"/> is true, page/pageSize are applied to rows
    /// but <see cref="CurrentStockSummaryDto"/> totals are always computed from the
    /// complete filtered dataset (pagination-independent).
    /// </summary>
    private async Task<(List<CurrentStockRowDto> rows, CurrentStockSummaryDto summary)>
        BuildCurrentStockAsync(CurrentStockFilterDto filter, bool paginate)
    {
        // ── Base query ─────────────────────────────────────────────────
        var query = _context.Inventories
            .AsNoTracking()
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .AsQueryable();

        // ── Location filters ───────────────────────────────────────────
        if (filter.OutletId.HasValue)
        {
            query = query.Where(i => i.LocationType == "outlet" && i.LocationId == filter.OutletId.Value);
        }
        else if (filter.WarehouseId.HasValue)
        {
            query = query.Where(i => i.LocationType == "warehouse" && i.LocationId == filter.WarehouseId.Value);
        }

        // ── Category filter ────────────────────────────────────────────
        if (filter.CategoryId.HasValue)
            query = query.Where(i => i.Variant.Product.CategoryId == filter.CategoryId.Value);

        // ── Product filter ─────────────────────────────────────────────
        if (filter.ProductId.HasValue)
            query = query.Where(i => i.Variant.ProductId == filter.ProductId.Value);

        // ── Stock status filter ────────────────────────────────────────
        query = filter.StockStatus switch
        {
            "out-of-stock" => query.Where(i => i.Quantity == 0),
            "low-stock"    => query.Where(i => i.Quantity > 0 && i.Quantity <= i.LowStockThreshold),
            "in-stock"     => query.Where(i => i.Quantity > i.LowStockThreshold),
            _              => query
        };

        // ── Search filter (SKU, Barcode, Product Name) ─────────────────
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(i =>
                i.Variant.Sku.ToLower().Contains(search) ||
                (i.Variant.Barcode != null && i.Variant.Barcode.ToLower().Contains(search)) ||
                i.Variant.Product.Name.ToLower().Contains(search) ||
                (i.Variant.Product.ProductCode != null && i.Variant.Product.ProductCode.ToLower().Contains(search)) ||
                (i.Variant.Product.Barcode != null && i.Variant.Product.Barcode.ToLower().Contains(search)));
        }

        // ── Load filtered inventories ──────────────────────────────────
        var inventories = await query.ToListAsync();

        if (inventories.Count == 0)
        {
            return (new List<CurrentStockRowDto>(), new CurrentStockSummaryDto());
        }

        // ── Resolve location names ─────────────────────────────────────
        var outletIds    = inventories.Where(i => i.LocationType == "outlet").Select(i => i.LocationId).Distinct().ToList();
        var warehouseIds = inventories.Where(i => i.LocationType == "warehouse").Select(i => i.LocationId).Distinct().ToList();

        var outletNames = outletIds.Count > 0
            ? await _context.Outlets.AsNoTracking()
                .Where(o => outletIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.Name)
            : new Dictionary<long, string>();

        var warehouseNames = warehouseIds.Count > 0
            ? await _context.Warehouses.AsNoTracking()
                .Where(w => warehouseIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name)
            : new Dictionary<long, string>();

        // ── Resolve last purchase dates per variant ────────────────────
        var variantIds = inventories.Select(i => i.VariantId).Distinct().ToList();

        var lastPurchaseDates = await (
            from gi  in _context.GrnItems.AsNoTracking()
            join grn in _context.Grns.AsNoTracking() on gi.GrnId equals grn.Id
            join poi in _context.PurchaseOrderItems.AsNoTracking() on gi.PoItemId equals poi.Id
            where variantIds.Contains(poi.VariantId)
            group grn.ReceivedDate by poi.VariantId into g
            select new { VariantId = g.Key, LastDate = g.Max() }
        ).ToDictionaryAsync(x => x.VariantId, x => x.LastDate);

        // ── Resolve last sale dates per variant ────────────────────────
        var lastSaleDates = await _context.SaleItems.AsNoTracking()
            .Where(si => variantIds.Contains(si.VariantId))
            .GroupBy(si => si.VariantId)
            .Select(g => new { VariantId = g.Key, LastDate = g.Max(si => si.Sale.SaleDate) })
            .ToDictionaryAsync(x => x.VariantId, x => x.LastDate);

        // ── Project to DTO rows ────────────────────────────────────────
        var allRows = inventories.Select(i =>
        {
            var locationName = i.LocationType == "outlet"
                ? (outletNames.TryGetValue(i.LocationId, out var n1) ? n1 : $"Outlet {i.LocationId}")
                : (warehouseNames.TryGetValue(i.LocationId, out var n2) ? n2 : $"Warehouse {i.LocationId}");

            var unitCost   = i.Variant.Product.CostPrice + i.Variant.CostAdjustment;
            var stockValue = unitCost * i.Quantity;

            var stockStatus = i.Quantity == 0 ? "out-of-stock"
                : i.Quantity <= i.LowStockThreshold ? "low-stock"
                : "in-stock";

            lastPurchaseDates.TryGetValue(i.VariantId, out var lastPurchase);
            lastSaleDates.TryGetValue(i.VariantId, out var lastSale);

            return new CurrentStockRowDto
            {
                VariantId         = i.VariantId,
                ProductCode       = i.Variant.Product.ProductCode ?? string.Empty,
                Barcode           = i.Variant.Barcode ?? i.Variant.Product.Barcode,
                ProductName       = i.Variant.Product.Name,
                Category          = i.Variant.Product.Category?.Name ?? "Uncategorized",
                LocationId        = i.LocationId,
                LocationType      = i.LocationType,
                LocationName      = locationName,
                AvailableQuantity = i.Quantity,
                ReservedQuantity  = 0,
                ReorderLevel      = i.LowStockThreshold,
                UnitCost          = unitCost,
                StockValue        = stockValue,
                LastPurchaseDate  = lastPurchase == default ? null : lastPurchase,
                LastSaleDate      = lastSale == default ? null : lastSale,
                StockStatus       = stockStatus
            };
        }).ToList();

        // ── Sort ───────────────────────────────────────────────────────
        allRows = (filter.SortBy?.ToLower(), filter.SortDir?.ToLower()) switch
        {
            ("productcode", "desc")    => allRows.OrderByDescending(r => r.ProductCode).ToList(),
            ("productcode", _)         => allRows.OrderBy(r => r.ProductCode).ToList(),
            ("barcode", "desc")        => allRows.OrderByDescending(r => r.Barcode).ToList(),
            ("barcode", _)             => allRows.OrderBy(r => r.Barcode).ToList(),
            ("category", "desc")       => allRows.OrderByDescending(r => r.Category).ToList(),
            ("category", _)            => allRows.OrderBy(r => r.Category).ToList(),
            ("location", "desc")       => allRows.OrderByDescending(r => r.LocationName).ToList(),
            ("location", _)            => allRows.OrderBy(r => r.LocationName).ToList(),
            ("availablequantity","desc")=> allRows.OrderByDescending(r => r.AvailableQuantity).ToList(),
            ("availablequantity", _)   => allRows.OrderBy(r => r.AvailableQuantity).ToList(),
            ("stockvalue", "desc")     => allRows.OrderByDescending(r => r.StockValue).ToList(),
            ("stockvalue", _)          => allRows.OrderBy(r => r.StockValue).ToList(),
            ("unitcost", "desc")       => allRows.OrderByDescending(r => r.UnitCost).ToList(),
            ("unitcost", _)            => allRows.OrderBy(r => r.UnitCost).ToList(),
            _                          => allRows.OrderBy(r => r.ProductName).ToList()
        };

        // ── Summary — always from the FULL filtered set ────────────────
        var summary = new CurrentStockSummaryDto
        {
            TotalProducts  = allRows.Count,
            TotalQuantity  = allRows.Sum(r => r.AvailableQuantity),
            TotalStockValue = allRows.Sum(r => r.StockValue)
        };

        // ── Paginate (only when requested) ─────────────────────────────
        List<CurrentStockRowDto> pageRows;
        if (paginate && filter.PageSize > 0)
        {
            var skip = (filter.Page - 1) * filter.PageSize;
            pageRows = allRows.Skip(skip).Take(filter.PageSize).ToList();
        }
        else
        {
            pageRows = allRows;
        }

        return (pageRows, summary);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Product Ledger Report
    // ─────────────────────────────────────────────────────────────────────

    public async Task<ProductLedgerReportDto> GetProductLedgerReportAsync(ProductLedgerFilterDto filter)
    {
        var (rows, summary, productName, productSku, variantName) =
            await BuildProductLedgerAsync(filter, paginate: true);

        var totalCount = await CountProductLedgerRowsAsync(filter);

        return new ProductLedgerReportDto
        {
            ProductId   = filter.ProductId,
            ProductName = productName,
            ProductSku  = productSku,
            VariantId   = filter.VariantId,
            VariantName = variantName,
            Items       = rows,
            TotalCount  = totalCount,
            Summary     = summary
        };
    }

    public async Task<List<ProductLedgerRowDto>> GetProductLedgerExportAsync(ProductLedgerFilterDto filter)
    {
        var (rows, _, _, _, _) = await BuildProductLedgerAsync(filter, paginate: false);
        return rows;
    }

    /// <summary>Count total rows for the given filter (for pagination metadata).</summary>
    private async Task<int> CountProductLedgerRowsAsync(ProductLedgerFilterDto filter)
    {
        var variantIds = await ResolveVariantIdsAsync(filter.ProductId, filter.VariantId);
        var query = BuildLedgerBaseQuery(variantIds, filter);
        return await query.CountAsync();
    }

    private async Task<(
        List<ProductLedgerRowDto> rows,
        ProductLedgerSummaryDto summary,
        string productName,
        string? productSku,
        string? variantName)>
        BuildProductLedgerAsync(ProductLedgerFilterDto filter, bool paginate)
    {
        // ── Resolve product info ───────────────────────────────────────
        var product = await _context.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == filter.ProductId)
            ?? throw new KeyNotFoundException($"Product {filter.ProductId} not found.");

        string? variantName = null;
        decimal unitCost = product.CostPrice;

        if (filter.VariantId.HasValue)
        {
            var variant = await _context.ProductVariants.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == filter.VariantId.Value && v.ProductId == filter.ProductId)
                ?? throw new KeyNotFoundException($"Variant {filter.VariantId} does not belong to product {filter.ProductId}.");
            variantName = variant.Name;
            unitCost    = product.CostPrice + variant.CostAdjustment;
        }

        var variantIds = await ResolveVariantIdsAsync(filter.ProductId, filter.VariantId);

        // ── Date range ─────────────────────────────────────────────────
        DateTime? rangeStart = filter.DateFrom;
        DateTime? rangeEnd   = filter.DateTo.HasValue
            ? filter.DateTo.Value.Date.AddDays(1).AddTicks(-1)
            : null;

        // ── Opening balance (before window) ────────────────────────────
        var baseQuery = BuildLedgerBaseQuery(variantIds, filter);

        int opening = 0;
        if (rangeStart.HasValue)
        {
            opening = await _context.StockLedgers.AsNoTracking()
                .Where(sl => variantIds.Contains(sl.VariantId))
                .Where(sl => !filter.OutletId.HasValue    || (sl.LocationType == "outlet"    && sl.LocationId == filter.OutletId.Value))
                .Where(sl => !filter.WarehouseId.HasValue || (sl.LocationType == "warehouse" && sl.LocationId == filter.WarehouseId.Value))
                .Where(sl => sl.CreatedAt < rangeStart.Value)
                .SumAsync(sl => (int?)(sl.QtyIn - sl.QtyOut)) ?? 0;
        }

        // ── Load all in-window rows ────────────────────────────────────
        var ledgerRows = await baseQuery
            .OrderBy(sl => sl.CreatedAt)
            .ThenBy(sl => sl.Id)
            .Select(sl => new
            {
                sl.Id,
                sl.CreatedAt,
                sl.LocationId,
                sl.LocationType,
                sl.VariantId,
                sl.TransactionType,
                sl.ReferenceType,
                sl.ReferenceId,
                sl.QtyIn,
                sl.QtyOut,
                sl.BalanceAfter,
                sl.Remarks,
                VariantSku = sl.Variant.Sku,
                ProductName = sl.Variant.Product.Name,
                VariantCostAdj = sl.Variant.CostAdjustment,
                ProductCost = sl.Variant.Product.CostPrice,
                CreatedByName = sl.Creator != null ? sl.Creator.Name : null
            })
            .ToListAsync();

        if (ledgerRows.Count == 0)
        {
            return (new List<ProductLedgerRowDto>(),
                    new ProductLedgerSummaryDto { OpeningStock = opening, ClosingStock = opening },
                    product.Name, product.Sku, variantName);
        }

        // ── Resolve location names ────────────────────────────────────
        var outletIds    = ledgerRows.Where(r => r.LocationType == "outlet").Select(r => r.LocationId).Distinct().ToList();
        var warehouseIds = ledgerRows.Where(r => r.LocationType == "warehouse").Select(r => r.LocationId).Distinct().ToList();

        var outletNames = outletIds.Count > 0
            ? await _context.Outlets.AsNoTracking().Where(o => outletIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.Name)
            : new Dictionary<long, string>();

        var warehouseNames = warehouseIds.Count > 0
            ? await _context.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name)
            : new Dictionary<long, string>();

        // ── Resolve reference numbers ──────────────────────────────────
        var saleIds       = ledgerRows.Where(r => r.ReferenceType == StockLedgerReferenceType.Sale).Select(r => r.ReferenceId).Distinct().ToList();
        var grnIds        = ledgerRows.Where(r => r.ReferenceType == StockLedgerReferenceType.Grn).Select(r => r.ReferenceId).Distinct().ToList();
        var transferIds   = ledgerRows.Where(r => r.ReferenceType == StockLedgerReferenceType.StockTransfer).Select(r => r.ReferenceId).Distinct().ToList();
        var adjustIds     = ledgerRows.Where(r => r.ReferenceType == StockLedgerReferenceType.StockAdjustment).Select(r => r.ReferenceId).Distinct().ToList();

        var saleNumbers = saleIds.Count > 0
            ? await _context.Sales.AsNoTracking().Where(s => saleIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.SaleNumber)
            : new Dictionary<long, string>();

        var grnNumbers = grnIds.Count > 0
            ? await _context.Grns.AsNoTracking().Where(g => grnIds.Contains(g.Id))
                .Select(g => new { g.Id, g.PoId })
                .ToDictionaryAsync(g => g.Id, g => $"GRN-{g.Id} (PO-{g.PoId})")
            : new Dictionary<long, string>();

        // ── Project all rows ───────────────────────────────────────────
        var allRows = new List<ProductLedgerRowDto>(ledgerRows.Count);
        int totalIn  = 0;
        int totalOut = 0;
        decimal totalValue = 0m;

        foreach (var r in ledgerRows)
        {
            var rowUnitCost = r.ProductCost + r.VariantCostAdj;
            var qty         = r.QtyIn + r.QtyOut;
            var txValue     = rowUnitCost * qty;

            totalIn    += r.QtyIn;
            totalOut   += r.QtyOut;
            totalValue += txValue;

            var locationName = r.LocationType == "outlet"
                ? (outletNames.TryGetValue(r.LocationId, out var n1) ? n1 : $"Outlet {r.LocationId}")
                : (warehouseNames.TryGetValue(r.LocationId, out var n2) ? n2 : $"Warehouse {r.LocationId}");

            var refNo = r.ReferenceType switch
            {
                StockLedgerReferenceType.Sale            => saleNumbers.TryGetValue(r.ReferenceId, out var sn) ? sn : $"SALE-{r.ReferenceId}",
                StockLedgerReferenceType.Grn             => grnNumbers.TryGetValue(r.ReferenceId, out var gn) ? gn : $"GRN-{r.ReferenceId}",
                StockLedgerReferenceType.StockTransfer   => $"TRF-{r.ReferenceId}",
                StockLedgerReferenceType.StockAdjustment => $"ADJ-{r.ReferenceId}",
                _                                        => $"{r.ReferenceType}-{r.ReferenceId}"
            };

            allRows.Add(new ProductLedgerRowDto
            {
                LedgerId             = r.Id,
                TransactionDate      = r.CreatedAt,
                TransactionType      = r.TransactionType,
                TransactionTypeLabel = TransactionTypeLabel(r.TransactionType),
                ReferenceNumber      = refNo,
                VariantId            = r.VariantId,
                ProductName          = r.ProductName,
                Sku                  = r.VariantSku,
                LocationId           = r.LocationId,
                LocationName         = locationName,
                OpeningQuantity      = r.BalanceAfter - r.QtyIn + r.QtyOut,
                StockIn              = r.QtyIn,
                StockOut             = r.QtyOut,
                ClosingQuantity      = r.BalanceAfter,
                UnitCost             = rowUnitCost,
                TransactionValue     = txValue,
                PerformedBy          = r.CreatedByName,
                Remarks              = r.Remarks
            });
        }

        // ── Full-dataset summary ───────────────────────────────────────
        var summary = new ProductLedgerSummaryDto
        {
            OpeningStock         = opening,
            TotalStockIn         = totalIn,
            TotalStockOut        = totalOut,
            ClosingStock         = opening + totalIn - totalOut,
            TotalTransactionValue = totalValue
        };

        // ── Paginate ───────────────────────────────────────────────────
        List<ProductLedgerRowDto> pageRows;
        if (paginate && filter.PageSize > 0)
        {
            var skip = (filter.Page - 1) * filter.PageSize;
            pageRows = allRows.Skip(skip).Take(filter.PageSize).ToList();
        }
        else
        {
            pageRows = allRows;
        }

        return (pageRows, summary, product.Name, product.Sku, variantName);
    }

    private async Task<List<long>> ResolveVariantIdsAsync(long productId, long? variantId)
    {
        if (variantId.HasValue) return new List<long> { variantId.Value };
        return await _context.ProductVariants.AsNoTracking()
            .Where(v => v.ProductId == productId)
            .Select(v => v.Id)
            .ToListAsync();
    }

    private IQueryable<StockLedger> BuildLedgerBaseQuery(List<long> variantIds, ProductLedgerFilterDto filter)
    {
        var q = _context.StockLedgers.AsNoTracking()
            .Include(sl => sl.Variant).ThenInclude(v => v.Product)
            .Include(sl => sl.Creator)
            .Where(sl => variantIds.Contains(sl.VariantId));

        if (filter.OutletId.HasValue)
            q = q.Where(sl => sl.LocationType == "outlet" && sl.LocationId == filter.OutletId.Value);
        else if (filter.WarehouseId.HasValue)
            q = q.Where(sl => sl.LocationType == "warehouse" && sl.LocationId == filter.WarehouseId.Value);

        if (!string.IsNullOrWhiteSpace(filter.TransactionType))
            q = q.Where(sl => sl.TransactionType == filter.TransactionType);

        if (filter.DateFrom.HasValue)
            q = q.Where(sl => sl.CreatedAt >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
        {
            var end = filter.DateTo.Value.Date.AddDays(1).AddTicks(-1);
            q = q.Where(sl => sl.CreatedAt <= end);
        }

        return q;
    }

    private static string TransactionTypeLabel(string type) => type switch
    {
        StockLedgerTransactionType.Grn          => "Purchase (GRN)",
        StockLedgerTransactionType.Sale         => "Sale",
        StockLedgerTransactionType.Return       => "Sales Return",
        StockLedgerTransactionType.Adjustment   => "Stock Adjustment",
        StockLedgerTransactionType.TransferIn   => "Transfer In",
        StockLedgerTransactionType.TransferOut  => "Transfer Out",
        StockLedgerTransactionType.Exchange     => "Exchange",
        _                                       => type
    };

    // ─────────────────────────────────────────────────────────────────────
    // Stock Movement Report
    // ─────────────────────────────────────────────────────────────────────

    public async Task<StockMovementReportDto> GetStockMovementReportAsync(StockMovementFilterDto filter)
    {
        var (rows, summary, totalCount) = await BuildStockMovementAsync(filter, paginate: true);
        return new StockMovementReportDto { Items = rows, TotalCount = totalCount, Summary = summary };
    }

    public async Task<List<StockMovementRowDto>> GetStockMovementExportAsync(StockMovementFilterDto filter)
    {
        var (rows, _, _) = await BuildStockMovementAsync(filter, paginate: false);
        return rows;
    }

    private async Task<(List<StockMovementRowDto> rows, StockMovementSummaryDto summary, int totalCount)>
        BuildStockMovementAsync(StockMovementFilterDto filter, bool paginate)
    {
        var rangeStart = filter.DateFrom;
        var rangeEnd   = filter.DateTo.HasValue
            ? filter.DateTo.Value.Date.AddDays(1).AddTicks(-1)
            : (DateTime?)null;

        // ── Base ledger query for the selected period ──────────────────
        var ledgerQuery = _context.StockLedgers
            .AsNoTracking()
            .AsQueryable();

        if (rangeStart.HasValue)
            ledgerQuery = ledgerQuery.Where(sl => sl.CreatedAt >= rangeStart.Value);
        if (rangeEnd.HasValue)
            ledgerQuery = ledgerQuery.Where(sl => sl.CreatedAt <= rangeEnd.Value);

        if (filter.OutletId.HasValue)
            ledgerQuery = ledgerQuery.Where(sl => sl.LocationType == "outlet" && sl.LocationId == filter.OutletId.Value);
        else if (filter.WarehouseId.HasValue)
            ledgerQuery = ledgerQuery.Where(sl => sl.LocationType == "warehouse" && sl.LocationId == filter.WarehouseId.Value);

        if (filter.CategoryId.HasValue)
            ledgerQuery = ledgerQuery.Where(sl => sl.Variant.Product.CategoryId == filter.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            ledgerQuery = ledgerQuery.Where(sl =>
                sl.Variant.Sku.ToLower().Contains(s) ||
                sl.Variant.Product.Name.ToLower().Contains(s) ||
                (sl.Variant.Product.ProductCode != null && sl.Variant.Product.ProductCode.ToLower().Contains(s)));
        }

        // ── Project and aggregate in database ─────────────────────────
        var inRangeAggregates = await ledgerQuery
            .GroupBy(sl => new { sl.VariantId, sl.LocationId, sl.LocationType })
            .Select(g => new
            {
                g.Key.VariantId,
                g.Key.LocationId,
                g.Key.LocationType,
                StockIn  = g.Sum(sl => sl.QtyIn),
                StockOut = g.Sum(sl => sl.QtyOut)
            })
            .ToListAsync();

        if (inRangeAggregates.Count == 0)
            return (new List<StockMovementRowDto>(), new StockMovementSummaryDto(), 0);

        // ── Resolve variant + product info ────────────────────────────
        var variantIds  = inRangeAggregates.Select(r => r.VariantId).Distinct().ToList();
        var locationIds = inRangeAggregates.Select(r => r.LocationId).Distinct().ToList();

        var variantInfo = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product).ThenInclude(p => p.Category)
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new {
                v.Id,
                v.Sku,
                ProductName = v.Product.Name,
                VariantName = v.Name,
                VariantAttributes = v.Attributes,
                ProductCode = v.Product.ProductCode ?? string.Empty,
                CategoryName = v.Product.Category != null ? v.Product.Category.Name : "Uncategorized"
            })
            .ToDictionaryAsync(v => v.Id);

        // ── Compute opening balances (last BalanceAfter before rangeStart) ──
        Dictionary<(long VariantId, long LocationId), int> openingBalances;

        if (rangeStart.HasValue)
        {
            var priorRows = await _context.StockLedgers
                .AsNoTracking()
                .Where(sl => variantIds.Contains(sl.VariantId)
                          && locationIds.Contains(sl.LocationId)
                          && sl.CreatedAt < rangeStart.Value)
                .Select(sl => new { sl.VariantId, sl.LocationId, sl.BalanceAfter, sl.Id })
                .ToListAsync();

            openingBalances = priorRows
                .GroupBy(r => (r.VariantId, r.LocationId))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(r => r.Id).First().BalanceAfter);
        }
        else
        {
            openingBalances = new Dictionary<(long, long), int>();
        }

        // ── Resolve location names ─────────────────────────────────────
        var outletIds    = inRangeAggregates.Where(r => r.LocationType == "outlet").Select(r => r.LocationId).Distinct().ToList();
        var warehouseIds = inRangeAggregates.Where(r => r.LocationType == "warehouse").Select(r => r.LocationId).Distinct().ToList();

        var outletNames = outletIds.Count > 0
            ? await _context.Outlets.AsNoTracking().Where(o => outletIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name)
            : new Dictionary<long, string>();
        var warehouseNames = warehouseIds.Count > 0
            ? await _context.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name)
            : new Dictionary<long, string>();

        // ── Build DTO rows ────────────────────────────────────────────
        var allRows = inRangeAggregates.Select(agg =>
        {
            var opening = openingBalances.TryGetValue((agg.VariantId, agg.LocationId), out var ob) ? ob : 0;
            var closing = opening + agg.StockIn - agg.StockOut;
            var info    = variantInfo.TryGetValue(agg.VariantId, out var vi) ? vi : null;

            var locationName = agg.LocationType == "outlet"
                ? (outletNames.TryGetValue(agg.LocationId, out var n1) ? n1 : $"Outlet {agg.LocationId}")
                : (warehouseNames.TryGetValue(agg.LocationId, out var n2) ? n2 : $"Warehouse {agg.LocationId}");

            return new StockMovementRowDto
            {
                VariantId    = agg.VariantId,
                ProductCode  = info?.ProductCode  ?? string.Empty,
                Sku          = info?.Sku          ?? string.Empty,
                ProductName  = info?.ProductName  ?? string.Empty,
                VariantName  = info?.VariantName  ?? string.Empty,
                VariantAttributes = info?.VariantAttributes,
                CategoryName = info?.CategoryName ?? string.Empty,
                LocationId   = agg.LocationId,
                LocationType = agg.LocationType,
                LocationName = locationName,
                OpeningStock = opening,
                StockIn      = agg.StockIn,
                StockOut     = agg.StockOut,
                ClosingStock = closing,
                NetMovement  = closing - opening
            };
        }).ToList();

        // ── Sort ──────────────────────────────────────────────────────
        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("sku",         true)  => allRows.OrderByDescending(r => r.Sku).ToList(),
            ("sku",         false) => allRows.OrderBy(r => r.Sku).ToList(),
            ("category",    true)  => allRows.OrderByDescending(r => r.CategoryName).ToList(),
            ("category",    false) => allRows.OrderBy(r => r.CategoryName).ToList(),
            ("location",    true)  => allRows.OrderByDescending(r => r.LocationName).ToList(),
            ("location",    false) => allRows.OrderBy(r => r.LocationName).ToList(),
            ("opening",     true)  => allRows.OrderByDescending(r => r.OpeningStock).ToList(),
            ("opening",     false) => allRows.OrderBy(r => r.OpeningStock).ToList(),
            ("stockin",     true)  => allRows.OrderByDescending(r => r.StockIn).ToList(),
            ("stockin",     false) => allRows.OrderBy(r => r.StockIn).ToList(),
            ("stockout",    true)  => allRows.OrderByDescending(r => r.StockOut).ToList(),
            ("stockout",    false) => allRows.OrderBy(r => r.StockOut).ToList(),
            ("closing",     true)  => allRows.OrderByDescending(r => r.ClosingStock).ToList(),
            ("closing",     false) => allRows.OrderBy(r => r.ClosingStock).ToList(),
            ("netmovement", true)  => allRows.OrderByDescending(r => r.NetMovement).ToList(),
            ("netmovement", false) => allRows.OrderBy(r => r.NetMovement).ToList(),
            _                      => allRows.OrderBy(r => r.ProductName).ThenBy(r => r.Sku).ToList()
        };

        // ── Summary from ALL rows (pagination-independent) ────────────
        var summary = new StockMovementSummaryDto
        {
            TotalOpeningStock = allRows.Sum(r => r.OpeningStock),
            TotalStockIn      = allRows.Sum(r => r.StockIn),
            TotalStockOut     = allRows.Sum(r => r.StockOut),
            TotalClosingStock = allRows.Sum(r => r.ClosingStock),
            NetMovement       = allRows.Sum(r => r.NetMovement)
        };

        int totalCount = allRows.Count;

        if (paginate && filter.PageSize > 0)
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return (allRows, summary, totalCount);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Stock Valuation Report
    // ─────────────────────────────────────────────────────────────────────

    public async Task<StockValuationReportDto> GetStockValuationReportAsync(StockValuationFilterDto filter)
    {
        var (rows, summary, totalCount) = await BuildStockValuationAsync(filter, paginate: true);
        return new StockValuationReportDto { Items = rows, TotalCount = totalCount, Summary = summary };
    }

    public async Task<List<StockValuationRowDto>> GetStockValuationExportAsync(StockValuationFilterDto filter)
    {
        var (rows, _, _) = await BuildStockValuationAsync(filter, paginate: false);
        return rows;
    }

    private async Task<(List<StockValuationRowDto> rows, StockValuationSummaryDto summary, int totalCount)>
        BuildStockValuationAsync(StockValuationFilterDto filter, bool paginate)
    {
        var query = _context.Inventories
            .AsNoTracking()
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .AsQueryable();

        if (filter.OutletId.HasValue)
            query = query.Where(i => i.LocationType == "outlet" && i.LocationId == filter.OutletId.Value);
        else if (filter.WarehouseId.HasValue)
            query = query.Where(i => i.LocationType == "warehouse" && i.LocationId == filter.WarehouseId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(i => i.Variant.Product.CategoryId == filter.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(i =>
                i.Variant.Sku.ToLower().Contains(s) ||
                i.Variant.Product.Name.ToLower().Contains(s) ||
                (i.Variant.Product.ProductCode != null && i.Variant.Product.ProductCode.ToLower().Contains(s)) ||
                (i.Variant.Barcode != null && i.Variant.Barcode.ToLower().Contains(s)) ||
                (i.Variant.Product.Barcode != null && i.Variant.Product.Barcode.ToLower().Contains(s)));
        }

        var inventories = await query.ToListAsync();

        if (inventories.Count == 0)
            return (new List<StockValuationRowDto>(), new StockValuationSummaryDto(), 0);

        // ── Resolve location names ─────────────────────────────────────
        var outletIds    = inventories.Where(i => i.LocationType == "outlet").Select(i => i.LocationId).Distinct().ToList();
        var warehouseIds = inventories.Where(i => i.LocationType == "warehouse").Select(i => i.LocationId).Distinct().ToList();

        var outletNames = outletIds.Count > 0
            ? await _context.Outlets.AsNoTracking().Where(o => outletIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name)
            : new Dictionary<long, string>();
        var warehouseNames = warehouseIds.Count > 0
            ? await _context.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name)
            : new Dictionary<long, string>();

        // ── Project to DTO rows ────────────────────────────────────────
        var allRows = inventories.Select(i =>
        {
            var unitCost       = i.Variant.Product.CostPrice + i.Variant.CostAdjustment;
            var inventoryValue = unitCost * i.Quantity;
            var locationName   = i.LocationType == "outlet"
                ? (outletNames.TryGetValue(i.LocationId, out var n1) ? n1 : $"Outlet {i.LocationId}")
                : (warehouseNames.TryGetValue(i.LocationId, out var n2) ? n2 : $"Warehouse {i.LocationId}");

            return new StockValuationRowDto
            {
                VariantId      = i.VariantId,
                ProductCode    = i.Variant.Product.ProductCode ?? string.Empty,
                Barcode        = i.Variant.Barcode ?? i.Variant.Product.Barcode ?? string.Empty,
                Sku            = i.Variant.Sku,
                ProductName    = i.Variant.Product.Name,
                CategoryName   = i.Variant.Product.Category?.Name ?? "Uncategorized",
                LocationId     = i.LocationId,
                LocationType   = i.LocationType,
                LocationName   = locationName,
                Quantity       = i.Quantity,
                UnitCost       = unitCost,
                InventoryValue = inventoryValue
            };
        }).ToList();

        // ── Summary from ALL rows ──────────────────────────────────────
        var totalValue = allRows.Sum(r => r.InventoryValue);
        foreach (var r in allRows)
            r.PercentOfTotal = totalValue > 0 ? Math.Round(r.InventoryValue / totalValue * 100, 2) : 0;

        var byCategory = allRows
            .GroupBy(r => r.CategoryName)
            .Select(g => new StockValuationCategoryDto
            {
                CategoryName   = g.Key,
                Quantity       = g.Sum(r => r.Quantity),
                InventoryValue = g.Sum(r => r.InventoryValue),
                PercentOfTotal = totalValue > 0 ? Math.Round(g.Sum(r => r.InventoryValue) / totalValue * 100, 2) : 0
            })
            .OrderByDescending(c => c.InventoryValue)
            .ToList();

        var summary = new StockValuationSummaryDto
        {
            TotalProducts       = allRows.Select(r => r.VariantId).Distinct().Count(),
            TotalQuantity       = allRows.Sum(r => r.Quantity),
            TotalInventoryValue = totalValue,
            ByCategory          = byCategory
        };

        // ── Sort ──────────────────────────────────────────────────────
        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("productcode",    true)  => allRows.OrderByDescending(r => r.ProductCode).ToList(),
            ("productcode",    false) => allRows.OrderBy(r => r.ProductCode).ToList(),
            ("sku",            true)  => allRows.OrderByDescending(r => r.Sku).ToList(),
            ("sku",            false) => allRows.OrderBy(r => r.Sku).ToList(),
            ("category",       true)  => allRows.OrderByDescending(r => r.CategoryName).ToList(),
            ("category",       false) => allRows.OrderBy(r => r.CategoryName).ToList(),
            ("location",       true)  => allRows.OrderByDescending(r => r.LocationName).ToList(),
            ("location",       false) => allRows.OrderBy(r => r.LocationName).ToList(),
            ("quantity",       true)  => allRows.OrderByDescending(r => r.Quantity).ToList(),
            ("quantity",       false) => allRows.OrderBy(r => r.Quantity).ToList(),
            ("unitcost",       true)  => allRows.OrderByDescending(r => r.UnitCost).ToList(),
            ("unitcost",       false) => allRows.OrderBy(r => r.UnitCost).ToList(),
            ("inventoryvalue", false) => allRows.OrderBy(r => r.InventoryValue).ToList(),
            _                         => allRows.OrderByDescending(r => r.InventoryValue).ToList()
        };

        int totalCount = allRows.Count;

        if (paginate && filter.PageSize > 0)
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return (allRows, summary, totalCount);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Outlet Wise Stock Report
    // ─────────────────────────────────────────────────────────────────────

    public async Task<OutletWiseStockReportDto> GetOutletWiseStockReportAsync(OutletWiseStockFilterDto filter)
    {
        var (rows, summary, totalCount) = await BuildOutletWiseStockAsync(filter, paginate: true);
        return new OutletWiseStockReportDto { Items = rows, TotalCount = totalCount, Summary = summary };
    }

    public async Task<List<OutletWiseStockRowDto>> GetOutletWiseStockExportAsync(OutletWiseStockFilterDto filter)
    {
        var (rows, _, _) = await BuildOutletWiseStockAsync(filter, paginate: false);
        return rows;
    }

    private async Task<(List<OutletWiseStockRowDto> rows, OutletWiseStockSummaryDto summary, int totalCount)>
        BuildOutletWiseStockAsync(OutletWiseStockFilterDto filter, bool paginate)
    {
        // Outlet-only: stock at outlet locations
        var query = _context.Inventories
            .AsNoTracking()
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.LocationType == "outlet")
            .AsQueryable();

        if (filter.OutletId.HasValue)
            query = query.Where(i => i.LocationId == filter.OutletId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(i => i.Variant.Product.CategoryId == filter.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(i =>
                i.Variant.Sku.ToLower().Contains(s) ||
                i.Variant.Product.Name.ToLower().Contains(s) ||
                (i.Variant.Product.ProductCode != null && i.Variant.Product.ProductCode.ToLower().Contains(s)) ||
                (i.Variant.Barcode != null && i.Variant.Barcode.ToLower().Contains(s)));
        }

        var inventories = await query.ToListAsync();

        if (inventories.Count == 0)
            return (new List<OutletWiseStockRowDto>(), new OutletWiseStockSummaryDto(), 0);

        // Resolve outlet names
        var outletIds  = inventories.Select(i => i.LocationId).Distinct().ToList();
        var outletNames = await _context.Outlets.AsNoTracking()
            .Where(o => outletIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name);

        // Build all rows
        var allRows = inventories.Select(i =>
        {
            var unitCost   = i.Variant.Product.CostPrice + i.Variant.CostAdjustment;
            var outletName = outletNames.TryGetValue(i.LocationId, out var n) ? n : $"Outlet {i.LocationId}";
            return new OutletWiseStockRowDto
            {
                OutletId     = i.LocationId,
                OutletName   = outletName,
                VariantId    = i.VariantId,
                ProductCode  = i.Variant.Product.ProductCode ?? string.Empty,
                Barcode      = i.Variant.Barcode ?? i.Variant.Product.Barcode ?? string.Empty,
                Sku          = i.Variant.Sku,
                ProductName  = i.Variant.Product.Name,
                CategoryName = i.Variant.Product.Category?.Name ?? "Uncategorized",
                Quantity     = i.Quantity,
                UnitCost     = unitCost,
                StockValue   = unitCost * i.Quantity
            };
        }).ToList();

        // Summary from ALL rows
        var byOutlet = allRows
            .GroupBy(r => r.OutletId)
            .Select(g => new OutletStockSummaryDto
            {
                OutletId   = g.Key,
                OutletName = g.First().OutletName,
                TotalSkus  = g.Select(r => r.VariantId).Distinct().Count(),
                TotalQty   = g.Sum(r => r.Quantity),
                StockValue = g.Sum(r => r.StockValue)
            })
            .OrderBy(o => o.OutletName)
            .ToList();

        var summary = new OutletWiseStockSummaryDto
        {
            TotalOutlets    = byOutlet.Count,
            TotalSkus       = allRows.Select(r => r.VariantId).Distinct().Count(),
            TotalQuantity   = allRows.Sum(r => r.Quantity),
            TotalStockValue = allRows.Sum(r => r.StockValue),
            ByOutlet        = byOutlet
        };

        // Sort
        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("outletname",   true)  => allRows.OrderByDescending(r => r.OutletName).ToList(),
            ("outletname",   false) => allRows.OrderBy(r => r.OutletName).ThenBy(r => r.ProductName).ToList(),
            ("productname",  true)  => allRows.OrderByDescending(r => r.ProductName).ToList(),
            ("productname",  false) => allRows.OrderBy(r => r.ProductName).ToList(),
            ("sku",          true)  => allRows.OrderByDescending(r => r.Sku).ToList(),
            ("sku",          false) => allRows.OrderBy(r => r.Sku).ToList(),
            ("category",     true)  => allRows.OrderByDescending(r => r.CategoryName).ToList(),
            ("category",     false) => allRows.OrderBy(r => r.CategoryName).ToList(),
            ("quantity",     true)  => allRows.OrderByDescending(r => r.Quantity).ToList(),
            ("quantity",     false) => allRows.OrderBy(r => r.Quantity).ToList(),
            ("stockvalue",   true)  => allRows.OrderByDescending(r => r.StockValue).ToList(),
            ("stockvalue",   false) => allRows.OrderBy(r => r.StockValue).ToList(),
            _                       => allRows.OrderBy(r => r.OutletName).ThenBy(r => r.ProductName).ToList()
        };

        int totalCount = allRows.Count;

        if (paginate && filter.PageSize > 0)
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return (allRows, summary, totalCount);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Report #6: Low Stock Report
    // ──────────────────────────────────────────────────────────────────────

    public async Task<LowStockReportDto> GetLowStockReportAsync(LowStockFilterDto filter)
    {
        var (rows, summary, totalCount) = await BuildLowStockAsync(filter, paginate: true);
        return new LowStockReportDto { Items = rows, TotalCount = totalCount, Summary = summary };
    }

    public async Task<List<LowStockRowDto>> GetLowStockExportAsync(LowStockFilterDto filter)
    {
        var (rows, _, _) = await BuildLowStockAsync(filter, paginate: false);
        return rows;
    }

    private async Task<(List<LowStockRowDto> rows, LowStockSummaryDto summary, int totalCount)>
        BuildLowStockAsync(LowStockFilterDto filter, bool paginate)
    {
        var query = _context.Inventories
            .AsNoTracking()
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            // Low stock: quantity > 0 but <= reorder level (negative handled separately)
            .Where(i => i.Quantity > 0 && i.Quantity <= i.LowStockThreshold)
            .AsQueryable();

        if (filter.OutletId.HasValue)
            query = query.Where(i => i.LocationType == "outlet" && i.LocationId == filter.OutletId.Value);
        else if (filter.WarehouseId.HasValue)
            query = query.Where(i => i.LocationType == "warehouse" && i.LocationId == filter.WarehouseId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(i => i.Variant.Product.CategoryId == filter.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(i =>
                i.Variant.Sku.ToLower().Contains(s) ||
                i.Variant.Product.Name.ToLower().Contains(s) ||
                (i.Variant.Product.ProductCode != null && i.Variant.Product.ProductCode.ToLower().Contains(s)) ||
                (i.Variant.Barcode != null && i.Variant.Barcode.ToLower().Contains(s)));
        }

        var inventories = await query.ToListAsync();

        if (inventories.Count == 0)
            return (new List<LowStockRowDto>(), new LowStockSummaryDto(), 0);

        var outletIds     = inventories.Where(i => i.LocationType == "outlet").Select(i => i.LocationId).Distinct().ToList();
        var warehouseIds  = inventories.Where(i => i.LocationType == "warehouse").Select(i => i.LocationId).Distinct().ToList();
        var outletNames   = await _context.Outlets.AsNoTracking().Where(o => outletIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name);
        var warehouseNames = await _context.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name);

        var allRows = inventories.Select(i =>
        {
            var locName = i.LocationType == "outlet"
                ? (outletNames.TryGetValue(i.LocationId, out var n1) ? n1 : $"Outlet {i.LocationId}")
                : (warehouseNames.TryGetValue(i.LocationId, out var n2) ? n2 : $"Warehouse {i.LocationId}");

            var deficit = Math.Max(0, i.LowStockThreshold - i.Quantity);
            var urgency = i.Quantity == 0 ? "Critical"
                        : i.Quantity <= i.LowStockThreshold / 2 ? "Low"
                        : "Below Reorder";

            return new LowStockRowDto
            {
                VariantId         = i.VariantId,
                ProductCode       = i.Variant.Product.ProductCode ?? string.Empty,
                Barcode           = i.Variant.Barcode ?? i.Variant.Product.Barcode ?? string.Empty,
                Sku               = i.Variant.Sku,
                ProductName       = i.Variant.Product.Name,
                CategoryName      = i.Variant.Product.Category?.Name ?? "Uncategorized",
                LocationId        = i.LocationId,
                LocationType      = i.LocationType,
                LocationName      = locName,
                CurrentStock      = i.Quantity,
                ReorderLevel      = i.LowStockThreshold,
                SuggestedOrderQty = deficit,
                UrgencyLevel      = urgency
            };
        }).ToList();

        // Summary from ALL rows
        var summary = new LowStockSummaryDto
        {
            TotalSkus       = allRows.Select(r => r.VariantId).Distinct().Count(),
            TotalDeficitQty = allRows.Sum(r => r.SuggestedOrderQty),
            CriticalCount   = allRows.Count(r => r.UrgencyLevel == "Critical"),
            LowCount        = allRows.Count(r => r.UrgencyLevel == "Low")
        };

        // Sort
        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("urgencylevel",      true)  => allRows.OrderByDescending(r => r.UrgencyLevel).ToList(),
            ("urgencylevel",      false) => allRows.OrderBy(r => r.UrgencyLevel).ThenBy(r => r.CurrentStock).ToList(),
            ("productname",       true)  => allRows.OrderByDescending(r => r.ProductName).ToList(),
            ("productname",       false) => allRows.OrderBy(r => r.ProductName).ToList(),
            ("sku",               true)  => allRows.OrderByDescending(r => r.Sku).ToList(),
            ("sku",               false) => allRows.OrderBy(r => r.Sku).ToList(),
            ("category",          true)  => allRows.OrderByDescending(r => r.CategoryName).ToList(),
            ("category",          false) => allRows.OrderBy(r => r.CategoryName).ToList(),
            ("currentstock",      true)  => allRows.OrderByDescending(r => r.CurrentStock).ToList(),
            ("currentstock",      false) => allRows.OrderBy(r => r.CurrentStock).ToList(),
            ("reorderlevel",      true)  => allRows.OrderByDescending(r => r.ReorderLevel).ToList(),
            ("reorderlevel",      false) => allRows.OrderBy(r => r.ReorderLevel).ToList(),
            ("suggestedorderqty", true)  => allRows.OrderByDescending(r => r.SuggestedOrderQty).ToList(),
            ("suggestedorderqty", false) => allRows.OrderBy(r => r.SuggestedOrderQty).ToList(),
            _                            => allRows.OrderBy(r => r.CurrentStock).ThenByDescending(r => r.SuggestedOrderQty).ToList()
        };

        int totalCount = allRows.Count;
        if (paginate && filter.PageSize > 0)
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return (allRows, summary, totalCount);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Report #7: Out Of Stock Report
    // ──────────────────────────────────────────────────────────────────────

    public async Task<OutOfStockReportDto> GetOutOfStockReportAsync(OutOfStockFilterDto filter)
    {
        var (rows, summary, totalCount) = await BuildOutOfStockAsync(filter, paginate: true);
        return new OutOfStockReportDto { Items = rows, TotalCount = totalCount, Summary = summary };
    }

    public async Task<List<OutOfStockRowDto>> GetOutOfStockExportAsync(OutOfStockFilterDto filter)
    {
        var (rows, _, _) = await BuildOutOfStockAsync(filter, paginate: false);
        return rows;
    }

    private async Task<(List<OutOfStockRowDto> rows, OutOfStockSummaryDto summary, int totalCount)>
        BuildOutOfStockAsync(OutOfStockFilterDto filter, bool paginate)
    {
        var query = _context.Inventories
            .AsNoTracking()
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.Quantity == 0)
            .AsQueryable();

        if (filter.OutletId.HasValue)
            query = query.Where(i => i.LocationType == "outlet" && i.LocationId == filter.OutletId.Value);
        else if (filter.WarehouseId.HasValue)
            query = query.Where(i => i.LocationType == "warehouse" && i.LocationId == filter.WarehouseId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(i => i.Variant.Product.CategoryId == filter.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(i =>
                i.Variant.Sku.ToLower().Contains(s) ||
                i.Variant.Product.Name.ToLower().Contains(s) ||
                (i.Variant.Product.ProductCode != null && i.Variant.Product.ProductCode.ToLower().Contains(s)) ||
                (i.Variant.Barcode != null && i.Variant.Barcode.ToLower().Contains(s)));
        }

        var inventories = await query.ToListAsync();

        if (inventories.Count == 0)
            return (new List<OutOfStockRowDto>(), new OutOfStockSummaryDto(), 0);

        var outletIds     = inventories.Where(i => i.LocationType == "outlet").Select(i => i.LocationId).Distinct().ToList();
        var warehouseIds  = inventories.Where(i => i.LocationType == "warehouse").Select(i => i.LocationId).Distinct().ToList();
        var outletNames   = await _context.Outlets.AsNoTracking().Where(o => outletIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name);
        var warehouseNames = await _context.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name);

        var allRows = inventories.Select(i =>
        {
            var locName = i.LocationType == "outlet"
                ? (outletNames.TryGetValue(i.LocationId, out var n1) ? n1 : $"Outlet {i.LocationId}")
                : (warehouseNames.TryGetValue(i.LocationId, out var n2) ? n2 : $"Warehouse {i.LocationId}");

            var unitCost = i.Variant.Product.CostPrice + i.Variant.CostAdjustment;

            return new OutOfStockRowDto
            {
                VariantId    = i.VariantId,
                ProductCode  = i.Variant.Product.ProductCode ?? string.Empty,
                Barcode      = i.Variant.Barcode ?? i.Variant.Product.Barcode ?? string.Empty,
                Sku          = i.Variant.Sku,
                ProductName  = i.Variant.Product.Name,
                CategoryName = i.Variant.Product.Category?.Name ?? "Uncategorized",
                LocationId   = i.LocationId,
                LocationType = i.LocationType,
                LocationName = locName,
                ReorderLevel = i.LowStockThreshold,
                UnitCost     = unitCost
            };
        }).ToList();

        // Summary from ALL rows
        var summary = new OutOfStockSummaryDto
        {
            TotalSkus          = allRows.Select(r => r.VariantId).Distinct().Count(),
            TotalLocations     = allRows.Select(r => r.LocationId).Distinct().Count(),
            EstimatedCostImpact = allRows.Sum(r => r.UnitCost * r.ReorderLevel)
        };

        // Sort
        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("productname",  true)  => allRows.OrderByDescending(r => r.ProductName).ToList(),
            ("productname",  false) => allRows.OrderBy(r => r.ProductName).ToList(),
            ("sku",          true)  => allRows.OrderByDescending(r => r.Sku).ToList(),
            ("sku",          false) => allRows.OrderBy(r => r.Sku).ToList(),
            ("category",     true)  => allRows.OrderByDescending(r => r.CategoryName).ToList(),
            ("category",     false) => allRows.OrderBy(r => r.CategoryName).ToList(),
            ("location",     true)  => allRows.OrderByDescending(r => r.LocationName).ToList(),
            ("location",     false) => allRows.OrderBy(r => r.LocationName).ToList(),
            ("reorderlevel", true)  => allRows.OrderByDescending(r => r.ReorderLevel).ToList(),
            ("reorderlevel", false) => allRows.OrderBy(r => r.ReorderLevel).ToList(),
            _                       => allRows.OrderBy(r => r.ProductName).ToList()
        };

        int totalCount = allRows.Count;
        if (paginate && filter.PageSize > 0)
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return (allRows, summary, totalCount);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Report #8: Negative Stock Report
    // ──────────────────────────────────────────────────────────────────────

    public async Task<NegativeStockReportDto> GetNegativeStockReportAsync(NegativeStockFilterDto filter)
    {
        var (rows, summary, totalCount) = await BuildNegativeStockAsync(filter, paginate: true);
        return new NegativeStockReportDto { Items = rows, TotalCount = totalCount, Summary = summary };
    }

    public async Task<List<NegativeStockRowDto>> GetNegativeStockExportAsync(NegativeStockFilterDto filter)
    {
        var (rows, _, _) = await BuildNegativeStockAsync(filter, paginate: false);
        return rows;
    }

    private async Task<(List<NegativeStockRowDto> rows, NegativeStockSummaryDto summary, int totalCount)>
        BuildNegativeStockAsync(NegativeStockFilterDto filter, bool paginate)
    {
        var query = _context.Inventories
            .AsNoTracking()
            .Include(i => i.Variant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
            .Where(i => i.Quantity < 0)
            .AsQueryable();

        if (filter.OutletId.HasValue)
            query = query.Where(i => i.LocationType == "outlet" && i.LocationId == filter.OutletId.Value);
        else if (filter.WarehouseId.HasValue)
            query = query.Where(i => i.LocationType == "warehouse" && i.LocationId == filter.WarehouseId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(i => i.Variant.Product.CategoryId == filter.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(i =>
                i.Variant.Sku.ToLower().Contains(s) ||
                i.Variant.Product.Name.ToLower().Contains(s) ||
                (i.Variant.Product.ProductCode != null && i.Variant.Product.ProductCode.ToLower().Contains(s)) ||
                (i.Variant.Barcode != null && i.Variant.Barcode.ToLower().Contains(s)));
        }

        var inventories = await query.ToListAsync();

        if (inventories.Count == 0)
            return (new List<NegativeStockRowDto>(), new NegativeStockSummaryDto(), 0);

        var outletIds     = inventories.Where(i => i.LocationType == "outlet").Select(i => i.LocationId).Distinct().ToList();
        var warehouseIds  = inventories.Where(i => i.LocationType == "warehouse").Select(i => i.LocationId).Distinct().ToList();
        var outletNames   = await _context.Outlets.AsNoTracking().Where(o => outletIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name);
        var warehouseNames = await _context.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name);

        var allRows = inventories.Select(i =>
        {
            var locName  = i.LocationType == "outlet"
                ? (outletNames.TryGetValue(i.LocationId, out var n1) ? n1 : $"Outlet {i.LocationId}")
                : (warehouseNames.TryGetValue(i.LocationId, out var n2) ? n2 : $"Warehouse {i.LocationId}");

            var unitCost   = i.Variant.Product.CostPrice + i.Variant.CostAdjustment;
            var stockValue = unitCost * i.Quantity; // will be negative

            return new NegativeStockRowDto
            {
                VariantId    = i.VariantId,
                ProductCode  = i.Variant.Product.ProductCode ?? string.Empty,
                Barcode      = i.Variant.Barcode ?? i.Variant.Product.Barcode ?? string.Empty,
                Sku          = i.Variant.Sku,
                ProductName  = i.Variant.Product.Name,
                CategoryName = i.Variant.Product.Category?.Name ?? "Uncategorized",
                LocationId   = i.LocationId,
                LocationType = i.LocationType,
                LocationName = locName,
                CurrentStock = i.Quantity,
                UnitCost     = unitCost,
                StockValue   = stockValue
            };
        }).ToList();

        // Summary from ALL rows
        var summary = new NegativeStockSummaryDto
        {
            TotalSkus          = allRows.Select(r => r.VariantId).Distinct().Count(),
            TotalNegativeQty   = allRows.Sum(r => r.CurrentStock),  // already negative
            TotalNegativeValue = allRows.Sum(r => r.StockValue)
        };

        // Sort
        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("currentstock",  true)  => allRows.OrderByDescending(r => r.CurrentStock).ToList(),
            ("currentstock",  false) => allRows.OrderBy(r => r.CurrentStock).ToList(),
            ("productname",   true)  => allRows.OrderByDescending(r => r.ProductName).ToList(),
            ("productname",   false) => allRows.OrderBy(r => r.ProductName).ToList(),
            ("sku",           true)  => allRows.OrderByDescending(r => r.Sku).ToList(),
            ("sku",           false) => allRows.OrderBy(r => r.Sku).ToList(),
            ("category",      true)  => allRows.OrderByDescending(r => r.CategoryName).ToList(),
            ("category",      false) => allRows.OrderBy(r => r.CategoryName).ToList(),
            ("stockvalue",    true)  => allRows.OrderByDescending(r => r.StockValue).ToList(),
            ("stockvalue",    false) => allRows.OrderBy(r => r.StockValue).ToList(),
            _                        => allRows.OrderBy(r => r.CurrentStock).ToList() // most negative first
        };

        int totalCount = allRows.Count;
        if (paginate && filter.PageSize > 0)
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return (allRows, summary, totalCount);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Report #9: Stock Adjustment Report
    // ──────────────────────────────────────────────────────────────────────

    public async Task<StockAdjustmentReportDto> GetStockAdjustmentReportAsync(StockAdjustmentFilterDto filter)
    {
        var (rows, summary, totalCount) = await BuildStockAdjustmentAsync(filter, paginate: true);
        return new StockAdjustmentReportDto { Items = rows, TotalCount = totalCount, Summary = summary };
    }

    public async Task<List<StockAdjustmentRowDto>> GetStockAdjustmentExportAsync(StockAdjustmentFilterDto filter)
    {
        var (rows, _, _) = await BuildStockAdjustmentAsync(filter, paginate: false);
        return rows;
    }

    private async Task<(List<StockAdjustmentRowDto> rows, StockAdjustmentSummaryDto summary, int totalCount)>
        BuildStockAdjustmentAsync(StockAdjustmentFilterDto filter, bool paginate)
    {
        var query = _context.StockAdjustments
            .AsNoTracking()
            .Include(a => a.Lines)
                .ThenInclude(l => l.Variant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.Category)
            .Include(a => a.Adjuster)
            .AsQueryable();

        if (filter.OutletId.HasValue)
            query = query.Where(a => a.LocationType == "outlet" && a.LocationId == filter.OutletId.Value);
        else if (filter.WarehouseId.HasValue)
            query = query.Where(a => a.LocationType == "warehouse" && a.LocationId == filter.WarehouseId.Value);

        if (filter.CategoryId.HasValue)
            query = query.Where(a => a.Lines.Any(l => l.Variant.Product.CategoryId == filter.CategoryId.Value));

        if (filter.DateFrom.HasValue)
            query = query.Where(a => a.AdjustmentDate >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
            query = query.Where(a => a.AdjustmentDate < filter.DateTo.Value.AddDays(1));

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            query = query.Where(a =>
                a.Lines.Any(l =>
                    l.Variant.Sku.ToLower().Contains(s) ||
                    l.Variant.Product.Name.ToLower().Contains(s) ||
                    (l.Variant.Product.ProductCode != null && l.Variant.Product.ProductCode.ToLower().Contains(s)) ||
                    l.Reason.ToLower().Contains(s)));
        }

        var adjustments = await query.ToListAsync();

        if (adjustments.Count == 0)
            return (new List<StockAdjustmentRowDto>(), new StockAdjustmentSummaryDto(), 0);

        var outletIds     = adjustments.Where(a => a.LocationType == "outlet").Select(a => a.LocationId).Distinct().ToList();
        var warehouseIds  = adjustments.Where(a => a.LocationType == "warehouse").Select(a => a.LocationId).Distinct().ToList();
        var outletNames   = await _context.Outlets.AsNoTracking().Where(o => outletIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name);
        var warehouseNames = await _context.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name);

        var allRows = adjustments
            .SelectMany(a =>
            {
                var locName = a.LocationType == "outlet"
                    ? (outletNames.TryGetValue(a.LocationId, out var n1) ? n1 : $"Outlet {a.LocationId}")
                    : (warehouseNames.TryGetValue(a.LocationId, out var n2) ? n2 : $"Warehouse {a.LocationId}");

                var userName = a.Adjuster?.Name ?? string.Empty;

                return a.Lines.Select(line => new StockAdjustmentRowDto
                {
                    Id             = a.Id,
                    AdjustmentDate = a.AdjustmentDate,
                    ProductCode    = line.Variant?.Product?.ProductCode ?? string.Empty,
                    Barcode        = line.Variant?.Barcode ?? line.Variant?.Product?.Barcode ?? string.Empty,
                    Sku            = line.Variant?.Sku ?? string.Empty,
                    ProductName    = line.Variant?.Product?.Name ?? string.Empty,
                    CategoryName   = line.Variant?.Product?.Category?.Name ?? "Uncategorized",
                    LocationId     = a.LocationId,
                    LocationType   = a.LocationType,
                    LocationName   = locName,
                    AdjustmentType = line.QuantityChange >= 0 ? "Addition" : "Reduction",
                    QuantityChange = line.QuantityChange,
                    Reason         = line.Reason,
                    AdjustedBy     = userName
                });
            })
            .ToList();

        // Summary from ALL rows
        var additions  = allRows.Where(r => r.QuantityChange >= 0).Sum(r => r.QuantityChange);
        var reductions = allRows.Where(r => r.QuantityChange < 0).Sum(r => r.QuantityChange);
        var summary = new StockAdjustmentSummaryDto
        {
            TotalAdjustments  = allRows.Count,
            TotalAdditions    = additions,
            TotalReductions   = Math.Abs(reductions),
            NetQuantityChange = additions + reductions
        };

        // Sort
        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("adjustmentdate",  true)  => allRows.OrderByDescending(r => r.AdjustmentDate).ToList(),
            ("adjustmentdate",  false) => allRows.OrderBy(r => r.AdjustmentDate).ToList(),
            ("productname",     true)  => allRows.OrderByDescending(r => r.ProductName).ToList(),
            ("productname",     false) => allRows.OrderBy(r => r.ProductName).ToList(),
            ("sku",             true)  => allRows.OrderByDescending(r => r.Sku).ToList(),
            ("sku",             false) => allRows.OrderBy(r => r.Sku).ToList(),
            ("adjustmenttype",  true)  => allRows.OrderByDescending(r => r.AdjustmentType).ToList(),
            ("adjustmenttype",  false) => allRows.OrderBy(r => r.AdjustmentType).ToList(),
            ("quantitychange",  true)  => allRows.OrderByDescending(r => r.QuantityChange).ToList(),
            ("quantitychange",  false) => allRows.OrderBy(r => r.QuantityChange).ToList(),
            ("location",        true)  => allRows.OrderByDescending(r => r.LocationName).ToList(),
            ("location",        false) => allRows.OrderBy(r => r.LocationName).ToList(),
            _                          => allRows.OrderByDescending(r => r.AdjustmentDate).ToList()
        };

        int totalCount = allRows.Count;
        if (paginate && filter.PageSize > 0)
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return (allRows, summary, totalCount);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Report #10: Stock Transfer Report
    // ──────────────────────────────────────────────────────────────────────

    public async Task<StockTransferReportDto> GetStockTransferReportAsync(StockTransferFilterDto filter)
    {
        var (rows, summary, totalCount) = await BuildStockTransferAsync(filter, paginate: true);
        return new StockTransferReportDto { Items = rows, TotalCount = totalCount, Summary = summary };
    }

    public async Task<List<StockTransferRowDto>> GetStockTransferReportExportAsync(StockTransferFilterDto filter)
    {
        var (rows, _, _) = await BuildStockTransferAsync(filter, paginate: false);
        return rows;
    }

    private async Task<(List<StockTransferRowDto> rows, StockTransferSummaryDto summary, int totalCount)>
        BuildStockTransferAsync(StockTransferFilterDto filter, bool paginate)
    {
        var transferQuery = _context.StockTransfers
            .AsNoTracking()
            .Include(t => t.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.Category)
            .Include(t => t.Creator)
            .AsQueryable();

        if (filter.DateFrom.HasValue)
            transferQuery = transferQuery.Where(t => t.TransferDate >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
            transferQuery = transferQuery.Where(t => t.TransferDate < filter.DateTo.Value.AddDays(1));

        if (!string.IsNullOrWhiteSpace(filter.Status))
            transferQuery = transferQuery.Where(t => t.Status == filter.Status.ToLower());

        if (filter.FromOutletId.HasValue)
            transferQuery = transferQuery.Where(t => t.FromLocationType == "outlet" && t.FromLocationId == filter.FromOutletId.Value);

        if (filter.ToOutletId.HasValue)
            transferQuery = transferQuery.Where(t => t.ToLocationType == "outlet" && t.ToLocationId == filter.ToOutletId.Value);

        var transfers = await transferQuery.ToListAsync();

        if (transfers.Count == 0 || transfers.All(t => !t.Items.Any()))
            return (new List<StockTransferRowDto>(), new StockTransferSummaryDto(), 0);

        // Resolve all location names
        var allFromOutletIds    = transfers.Where(t => t.FromLocationType == "outlet").Select(t => t.FromLocationId).Distinct().ToList();
        var allToOutletIds      = transfers.Where(t => t.ToLocationType   == "outlet").Select(t => t.ToLocationId).Distinct().ToList();
        var allFromWhIds        = transfers.Where(t => t.FromLocationType == "warehouse").Select(t => t.FromLocationId).Distinct().ToList();
        var allToWhIds          = transfers.Where(t => t.ToLocationType   == "warehouse").Select(t => t.ToLocationId).Distinct().ToList();

        var allOutletIds   = allFromOutletIds.Union(allToOutletIds).Distinct().ToList();
        var allWhIds       = allFromWhIds.Union(allToWhIds).Distinct().ToList();

        var outletNames   = await _context.Outlets.AsNoTracking().Where(o => allOutletIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name);
        var whNames       = await _context.Warehouses.AsNoTracking().Where(w => allWhIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name);

        string ResolveName(long id, string type) =>
            type == "outlet"
                ? (outletNames.TryGetValue(id, out var n1) ? n1 : $"Outlet {id}")
                : (whNames.TryGetValue(id, out var n2) ? n2 : $"Warehouse {id}");

        // Flatten transfers → one row per item, filtered by category/search
        var allRows = new List<StockTransferRowDto>();
        foreach (var t in transfers)
        {
            var creatorName = t.Creator != null ? t.Creator.Name : string.Empty;

            var fromName = ResolveName(t.FromLocationId, t.FromLocationType);
            var toName   = ResolveName(t.ToLocationId,   t.ToLocationType);

            foreach (var item in t.Items)
            {
                if (filter.CategoryId.HasValue && item.Variant.Product.CategoryId != filter.CategoryId.Value)
                    continue;

                if (!string.IsNullOrWhiteSpace(filter.Search))
                {
                    var s = filter.Search.Trim().ToLower();
                    bool matches =
                        item.Variant.Sku.ToLower().Contains(s) ||
                        item.Variant.Product.Name.ToLower().Contains(s) ||
                        (item.Variant.Product.ProductCode?.ToLower().Contains(s) ?? false);
                    if (!matches) continue;
                }

                var unitCost = item.Variant.Product.CostPrice + item.Variant.CostAdjustment;
                allRows.Add(new StockTransferRowDto
                {
                    TransferId       = t.Id,
                    TransferDate     = t.TransferDate,
                    FromLocationName = fromName,
                    FromLocationType = t.FromLocationType,
                    ToLocationName   = toName,
                    ToLocationType   = t.ToLocationType,
                    Status           = t.Status,
                    ProductCode      = item.Variant.Product.ProductCode ?? string.Empty,
                    Barcode          = item.Variant.Barcode ?? item.Variant.Product.Barcode ?? string.Empty,
                    Sku              = item.Variant.Sku,
                    ProductName      = item.Variant.Product.Name,
                    CategoryName     = item.Variant.Product.Category?.Name ?? "Uncategorized",
                    Quantity         = item.Quantity,
                    UnitCost         = unitCost,
                    TransferValue    = unitCost * item.Quantity,
                    CreatedBy        = creatorName
                });
            }
        }

        // Summary from ALL rows
        var distinctTransferIds = allRows.Select(r => r.TransferId).Distinct().ToList();
        var summary = new StockTransferSummaryDto
        {
            TotalTransfers = distinctTransferIds.Count,
            TotalLines     = allRows.Count,
            TotalQuantity  = allRows.Sum(r => r.Quantity),
            TotalValue     = allRows.Sum(r => r.TransferValue),
            PendingCount   = transfers.Count(t => distinctTransferIds.Contains(t.Id) && t.Status == "pending"),
            CompletedCount = transfers.Count(t => distinctTransferIds.Contains(t.Id) && t.Status == "completed")
        };

        // Sort
        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("transferdate",  true)  => allRows.OrderByDescending(r => r.TransferDate).ToList(),
            ("transferdate",  false) => allRows.OrderBy(r => r.TransferDate).ToList(),
            ("productname",   true)  => allRows.OrderByDescending(r => r.ProductName).ToList(),
            ("productname",   false) => allRows.OrderBy(r => r.ProductName).ToList(),
            ("fromlocation",  true)  => allRows.OrderByDescending(r => r.FromLocationName).ToList(),
            ("fromlocation",  false) => allRows.OrderBy(r => r.FromLocationName).ToList(),
            ("tolocation",    true)  => allRows.OrderByDescending(r => r.ToLocationName).ToList(),
            ("tolocation",    false) => allRows.OrderBy(r => r.ToLocationName).ToList(),
            ("quantity",      true)  => allRows.OrderByDescending(r => r.Quantity).ToList(),
            ("quantity",      false) => allRows.OrderBy(r => r.Quantity).ToList(),
            ("transfervalue", true)  => allRows.OrderByDescending(r => r.TransferValue).ToList(),
            ("transfervalue", false) => allRows.OrderBy(r => r.TransferValue).ToList(),
            ("status",        true)  => allRows.OrderByDescending(r => r.Status).ToList(),
            ("status",        false) => allRows.OrderBy(r => r.Status).ToList(),
            _                        => allRows.OrderByDescending(r => r.TransferDate).ToList()
        };

        int totalCount = allRows.Count;
        if (paginate && filter.PageSize > 0)
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();

        return (allRows, summary, totalCount);
    }
}
