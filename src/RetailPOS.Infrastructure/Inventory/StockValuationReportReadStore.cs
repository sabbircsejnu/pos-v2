using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.InventoryReports;

public sealed class StockValuationReportReadStore(RetailPOSDbContext dbContext)
    : IStockValuationReportReadStore
{
    public async Task<StockValuationReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        StockValuationReportFilter filter,
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

        var inventoryQuery = dbContext.Inventories
            .AsNoTracking()
            .Where(inventory =>
                !filter.OutletId.HasValue && !filter.WarehouseId.HasValue
                ? inventory.LocationType == "outlet" && outletIds.Contains(inventory.LocationId)
                    || inventory.LocationType == "warehouse"
                        && warehouseIds.Contains(inventory.LocationId)
                : filter.OutletId.HasValue
                    ? inventory.LocationType == "outlet" && inventory.LocationId == filter.OutletId.Value
                    : inventory.LocationType == "warehouse"
                        && inventory.LocationId == filter.WarehouseId!.Value);

        if (filter.CategoryId.HasValue)
        {
            inventoryQuery = inventoryQuery.Where(inventory =>
                inventory.Variant.Product.CategoryId == filter.CategoryId.Value);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            inventoryQuery = inventoryQuery.Where(inventory =>
                inventory.Variant.Sku.ToLower().Contains(search)
                || inventory.Variant.Product.Name.ToLower().Contains(search)
                || inventory.Variant.Product.ProductCode != null
                    && inventory.Variant.Product.ProductCode.ToLower().Contains(search)
                || inventory.Variant.Barcode != null
                    && inventory.Variant.Barcode.ToLower().Contains(search)
                || inventory.Variant.Product.Barcode != null
                    && inventory.Variant.Product.Barcode.ToLower().Contains(search));
        }

        var inventoryRows = await inventoryQuery
            .Select(inventory => new
            {
                inventory.VariantId,
                inventory.LocationId,
                inventory.LocationType,
                inventory.Quantity,
                ProductCode = inventory.Variant.Product.ProductCode ?? string.Empty,
                Barcode = inventory.Variant.Barcode
                    ?? inventory.Variant.Product.Barcode
                    ?? string.Empty,
                inventory.Variant.Sku,
                ProductName = inventory.Variant.Product.Name,
                CategoryName = inventory.Variant.Product.Category != null
                    ? inventory.Variant.Product.Category.Name
                    : "Uncategorized",
                inventory.Variant.Product.CostPrice,
                inventory.Variant.CostAdjustment
            })
            .ToListAsync(cancellationToken);

        if (inventoryRows.Count == 0)
        {
            return EmptyReport();
        }

        var outletLocations = inventoryRows
            .Where(row => row.LocationType == "outlet")
            .Select(row => row.LocationId)
            .Distinct()
            .ToList();
        var warehouseLocations = inventoryRows
            .Where(row => row.LocationType == "warehouse")
            .Select(row => row.LocationId)
            .Distinct()
            .ToList();
        var outletNames = outletLocations.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Outlets
                .AsNoTracking()
                .Where(outlet => outletLocations.Contains(outlet.Id) && outletIds.Contains(outlet.Id))
                .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);
        var warehouseNames = warehouseLocations.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Warehouses
                .AsNoTracking()
                .Where(warehouse =>
                    warehouseLocations.Contains(warehouse.Id) && warehouseIds.Contains(warehouse.Id))
                .ToDictionaryAsync(
                    warehouse => warehouse.Id,
                    warehouse => warehouse.Name,
                    cancellationToken);

        var allRows = inventoryRows.Select(row =>
        {
            var unitCost = row.CostPrice + row.CostAdjustment;
            return new StockValuationRowReadModel(
                row.VariantId,
                row.ProductCode,
                row.Barcode,
                row.Sku,
                row.ProductName,
                row.CategoryName,
                row.LocationId,
                row.LocationType,
                row.LocationType == "outlet"
                    ? outletNames.GetValueOrDefault(row.LocationId, $"Outlet {row.LocationId}")
                    : warehouseNames.GetValueOrDefault(row.LocationId, $"Warehouse {row.LocationId}"),
                row.Quantity,
                unitCost,
                unitCost * row.Quantity,
                0m);
        }).ToList();

        var totalValue = allRows.Sum(row => row.InventoryValue);
        allRows = allRows
            .Select(row => row with
            {
                PercentOfTotal = totalValue > 0
                    ? Math.Round(row.InventoryValue / totalValue * 100, 2)
                    : 0
            })
            .ToList();
        var nonPcsVariants = await RetailPOS.Infrastructure.Uom.UomReportTotals.NonPcsVariantIdsAsync(dbContext, cancellationToken);
        var byCategory = allRows
            .GroupBy(row => row.CategoryName)
            .Select(group => new StockValuationCategoryReadModel(
                group.Key,
                group.Where(row => !nonPcsVariants.Contains(row.VariantId)).Sum(row => row.Quantity),
                group.Sum(row => row.InventoryValue),
                totalValue > 0
                    ? Math.Round(group.Sum(row => row.InventoryValue) / totalValue * 100, 2)
                    : 0))
            .OrderByDescending(category => category.InventoryValue)
            .ToArray();
        var summary = new StockValuationSummaryReadModel(
            allRows.Select(row => row.VariantId).Distinct().Count(),
            allRows.Where(row => !nonPcsVariants.Contains(row.VariantId)).Sum(row => row.Quantity),
            totalValue,
            byCategory);

        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("productcode", true) => allRows.OrderByDescending(row => row.ProductCode).ToList(),
            ("productcode", false) => allRows.OrderBy(row => row.ProductCode).ToList(),
            ("sku", true) => allRows.OrderByDescending(row => row.Sku).ToList(),
            ("sku", false) => allRows.OrderBy(row => row.Sku).ToList(),
            ("category", true) => allRows.OrderByDescending(row => row.CategoryName).ToList(),
            ("category", false) => allRows.OrderBy(row => row.CategoryName).ToList(),
            ("location", true) => allRows.OrderByDescending(row => row.LocationName).ToList(),
            ("location", false) => allRows.OrderBy(row => row.LocationName).ToList(),
            ("quantity", true) => allRows.OrderByDescending(row => row.Quantity).ToList(),
            ("quantity", false) => allRows.OrderBy(row => row.Quantity).ToList(),
            ("unitcost", true) => allRows.OrderByDescending(row => row.UnitCost).ToList(),
            ("unitcost", false) => allRows.OrderBy(row => row.UnitCost).ToList(),
            ("inventoryvalue", false) => allRows.OrderBy(row => row.InventoryValue).ToList(),
            _ => allRows.OrderByDescending(row => row.InventoryValue).ToList()
        };

        var totalCount = allRows.Count;
        if (filter.PageSize > 0)
        {
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        }

        return new StockValuationReportReadModel(allRows, totalCount, summary);
    }

    private static StockValuationReportReadModel EmptyReport() =>
        new(
            Array.Empty<StockValuationRowReadModel>(),
            0,
            new StockValuationSummaryReadModel(
                0,
                0,
                0m,
                Array.Empty<StockValuationCategoryReadModel>()));
}
