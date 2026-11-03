using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.InventoryReports;

public sealed class StockAdjustmentReportReadStore(RetailPOSDbContext dbContext)
    : IStockAdjustmentReportReadStore
{
    public async Task<StockAdjustmentReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        StockAdjustmentReportFilter filter,
        CancellationToken cancellationToken)
    {
        if (filter.OutletId.HasValue && filter.WarehouseId.HasValue)
        {
            throw new InvalidOperationException("Specify either outletId or warehouseId, not both.");
        }

        var outletIds = scope.OutletIds.ToList();
        var warehouseIds = scope.WarehouseIds.ToList();
        if (scope.BusinessId.HasValue)
        {
            var businessId = scope.BusinessId.Value;
            outletIds = await dbContext.Outlets
                .AsNoTracking()
                .Where(outlet => outlet.BusinessId == businessId && outletIds.Contains(outlet.Id))
                .Select(outlet => outlet.Id)
                .ToListAsync(cancellationToken);
            warehouseIds = await dbContext.Warehouses
                .AsNoTracking()
                .Where(warehouse => warehouse.BusinessId == businessId && warehouseIds.Contains(warehouse.Id))
                .Select(warehouse => warehouse.Id)
                .ToListAsync(cancellationToken);
        }

        if (filter.OutletId.HasValue && !outletIds.Contains(filter.OutletId.Value)
            || filter.WarehouseId.HasValue && !warehouseIds.Contains(filter.WarehouseId.Value))
        {
            return EmptyReport();
        }

        var query = dbContext.StockAdjustments
            .AsNoTracking()
            .Include(adjustment => adjustment.Lines)
                .ThenInclude(line => line.Variant)
                    .ThenInclude(variant => variant.Product)
                        .ThenInclude(product => product.Category)
            .Include(adjustment => adjustment.Adjuster)
            .Where(adjustment =>
                filter.OutletId.HasValue
                    ? adjustment.LocationType == "outlet" && adjustment.LocationId == filter.OutletId.Value
                    : filter.WarehouseId.HasValue
                        ? adjustment.LocationType == "warehouse"
                            && adjustment.LocationId == filter.WarehouseId.Value
                        : adjustment.LocationType == "outlet" && outletIds.Contains(adjustment.LocationId)
                            || adjustment.LocationType == "warehouse"
                                && warehouseIds.Contains(adjustment.LocationId));

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(adjustment =>
                adjustment.Lines.Any(line => line.Variant.Product.CategoryId == filter.CategoryId.Value));
        }

        if (filter.DateFrom.HasValue)
        {
            query = query.Where(adjustment => adjustment.AdjustmentDate >= filter.DateFrom.Value);
        }

        if (filter.DateTo.HasValue)
        {
            var dateToExclusive = filter.DateTo.Value.AddDays(1);
            query = query.Where(adjustment => adjustment.AdjustmentDate < dateToExclusive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(adjustment =>
                adjustment.Lines.Any(line =>
                    line.Variant.Sku.ToLower().Contains(search)
                    || line.Variant.Product.Name.ToLower().Contains(search)
                    || line.Variant.Product.ProductCode != null
                        && line.Variant.Product.ProductCode.ToLower().Contains(search)
                    || line.Reason.ToLower().Contains(search)));
        }

        var adjustments = await query.ToListAsync(cancellationToken);
        if (adjustments.Count == 0)
        {
            return EmptyReport();
        }

        var outletLocationIds = adjustments
            .Where(adjustment => adjustment.LocationType == "outlet")
            .Select(adjustment => adjustment.LocationId)
            .Distinct()
            .ToList();
        var warehouseLocationIds = adjustments
            .Where(adjustment => adjustment.LocationType == "warehouse")
            .Select(adjustment => adjustment.LocationId)
            .Distinct()
            .ToList();
        var outletNames = outletLocationIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Outlets
                .AsNoTracking()
                .Where(outlet => outletLocationIds.Contains(outlet.Id) && outletIds.Contains(outlet.Id))
                .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);
        var warehouseNames = warehouseLocationIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Warehouses
                .AsNoTracking()
                .Where(warehouse =>
                    warehouseLocationIds.Contains(warehouse.Id) && warehouseIds.Contains(warehouse.Id))
                .ToDictionaryAsync(
                    warehouse => warehouse.Id,
                    warehouse => warehouse.Name,
                    cancellationToken);

        var allRows = adjustments
            .SelectMany(adjustment =>
            {
                var locationName = adjustment.LocationType == "outlet"
                    ? outletNames.GetValueOrDefault(
                        adjustment.LocationId,
                        $"Outlet {adjustment.LocationId}")
                    : warehouseNames.GetValueOrDefault(
                        adjustment.LocationId,
                        $"Warehouse {adjustment.LocationId}");
                var adjustedBy = adjustment.Adjuster?.Name ?? string.Empty;

                return adjustment.Lines.Select(line => new StockAdjustmentRowReadModel(
                    adjustment.Id,
                    adjustment.AdjustmentDate,
                    line.Variant?.Product?.ProductCode ?? string.Empty,
                    line.Variant?.Barcode
                        ?? line.Variant?.Product?.Barcode
                        ?? string.Empty,
                    line.Variant?.Sku ?? string.Empty,
                    line.Variant?.Product?.Name ?? string.Empty,
                    line.Variant?.Product?.Category?.Name ?? "Uncategorized",
                    adjustment.LocationId,
                    adjustment.LocationType,
                    locationName,
                    line.QuantityChange >= 0 ? "Addition" : "Reduction",
                    line.QuantityChange,
                    line.Reason,
                    adjustedBy));
            })
            .ToList();

        var additions = allRows.Where(row => row.QuantityChange >= 0).Sum(row => row.QuantityChange);
        var reductions = allRows.Where(row => row.QuantityChange < 0).Sum(row => row.QuantityChange);
        var summary = new StockAdjustmentSummaryReadModel(
            allRows.Count,
            additions,
            Math.Abs(reductions),
            additions + reductions);

        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("adjustmentdate", true) => allRows.OrderByDescending(row => row.AdjustmentDate).ToList(),
            ("adjustmentdate", false) => allRows.OrderBy(row => row.AdjustmentDate).ToList(),
            ("productname", true) => allRows.OrderByDescending(row => row.ProductName).ToList(),
            ("productname", false) => allRows.OrderBy(row => row.ProductName).ToList(),
            ("sku", true) => allRows.OrderByDescending(row => row.Sku).ToList(),
            ("sku", false) => allRows.OrderBy(row => row.Sku).ToList(),
            ("adjustmenttype", true) => allRows.OrderByDescending(row => row.AdjustmentType).ToList(),
            ("adjustmenttype", false) => allRows.OrderBy(row => row.AdjustmentType).ToList(),
            ("quantitychange", true) => allRows.OrderByDescending(row => row.QuantityChange).ToList(),
            ("quantitychange", false) => allRows.OrderBy(row => row.QuantityChange).ToList(),
            ("location", true) => allRows.OrderByDescending(row => row.LocationName).ToList(),
            ("location", false) => allRows.OrderBy(row => row.LocationName).ToList(),
            _ => allRows.OrderByDescending(row => row.AdjustmentDate).ToList()
        };

        var totalCount = allRows.Count;
        if (filter.PageSize > 0)
        {
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        }

        return new StockAdjustmentReportReadModel(allRows, totalCount, summary);
    }

    private static StockAdjustmentReportReadModel EmptyReport() =>
        new(
            Array.Empty<StockAdjustmentRowReadModel>(),
            0,
            new StockAdjustmentSummaryReadModel(0, 0, 0, 0));
}
