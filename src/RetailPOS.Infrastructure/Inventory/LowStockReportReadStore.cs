using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.InventoryReports;

public sealed class LowStockReportReadStore(RetailPOSDbContext dbContext)
    : ILowStockReportReadStore
{
    public async Task<LowStockReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        LowStockReportFilter filter,
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
                inventory.Quantity > 0
                && inventory.Quantity <= inventory.LowStockThreshold
                && (filter.OutletId.HasValue
                    ? inventory.LocationType == "outlet" && inventory.LocationId == filter.OutletId.Value
                    : filter.WarehouseId.HasValue
                        ? inventory.LocationType == "warehouse" && inventory.LocationId == filter.WarehouseId.Value
                        : inventory.LocationType == "outlet" && outletIds.Contains(inventory.LocationId)
                            || inventory.LocationType == "warehouse"
                                && warehouseIds.Contains(inventory.LocationId)));

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
                    && inventory.Variant.Barcode.ToLower().Contains(search));
        }

        var inventoryRows = await inventoryQuery
            .Select(inventory => new
            {
                inventory.VariantId,
                inventory.LocationId,
                inventory.LocationType,
                inventory.Quantity,
                inventory.LowStockThreshold,
                ProductCode = inventory.Variant.Product.ProductCode ?? string.Empty,
                Barcode = inventory.Variant.Barcode
                    ?? inventory.Variant.Product.Barcode
                    ?? string.Empty,
                inventory.Variant.Sku,
                ProductName = inventory.Variant.Product.Name,
                CategoryName = inventory.Variant.Product.Category != null
                    ? inventory.Variant.Product.Category.Name
                    : "Uncategorized"
            })
            .ToListAsync(cancellationToken);

        if (inventoryRows.Count == 0)
        {
            return EmptyReport();
        }

        var outletLocationIds = inventoryRows
            .Where(row => row.LocationType == "outlet")
            .Select(row => row.LocationId)
            .Distinct()
            .ToList();
        var warehouseLocationIds = inventoryRows
            .Where(row => row.LocationType == "warehouse")
            .Select(row => row.LocationId)
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

        var allRows = inventoryRows.Select(row =>
        {
            var deficit = Math.Max(0, row.LowStockThreshold - row.Quantity);
            var urgency = row.Quantity == 0
                ? "Critical"
                : row.Quantity <= row.LowStockThreshold / 2
                    ? "Low"
                    : "Below Reorder";
            var locationName = row.LocationType == "outlet"
                ? outletNames.GetValueOrDefault(row.LocationId, $"Outlet {row.LocationId}")
                : warehouseNames.GetValueOrDefault(row.LocationId, $"Warehouse {row.LocationId}");

            return new LowStockRowReadModel(
                row.VariantId,
                row.ProductCode,
                row.Barcode,
                row.Sku,
                row.ProductName,
                row.CategoryName,
                row.LocationId,
                row.LocationType,
                locationName,
                row.Quantity,
                row.LowStockThreshold,
                deficit,
                urgency);
        }).ToList();

        var summary = new LowStockSummaryReadModel(
            allRows.Select(row => row.VariantId).Distinct().Count(),
            allRows.Sum(row => row.SuggestedOrderQty),
            allRows.Count(row => row.UrgencyLevel == "Critical"),
            allRows.Count(row => row.UrgencyLevel == "Low"));

        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("urgencylevel", true) => allRows.OrderByDescending(row => row.UrgencyLevel).ToList(),
            ("urgencylevel", false) => allRows.OrderBy(row => row.UrgencyLevel).ThenBy(row => row.CurrentStock).ToList(),
            ("productname", true) => allRows.OrderByDescending(row => row.ProductName).ToList(),
            ("productname", false) => allRows.OrderBy(row => row.ProductName).ToList(),
            ("sku", true) => allRows.OrderByDescending(row => row.Sku).ToList(),
            ("sku", false) => allRows.OrderBy(row => row.Sku).ToList(),
            ("category", true) => allRows.OrderByDescending(row => row.CategoryName).ToList(),
            ("category", false) => allRows.OrderBy(row => row.CategoryName).ToList(),
            ("currentstock", true) => allRows.OrderByDescending(row => row.CurrentStock).ToList(),
            ("currentstock", false) => allRows.OrderBy(row => row.CurrentStock).ToList(),
            ("reorderlevel", true) => allRows.OrderByDescending(row => row.ReorderLevel).ToList(),
            ("reorderlevel", false) => allRows.OrderBy(row => row.ReorderLevel).ToList(),
            ("suggestedorderqty", true) => allRows.OrderByDescending(row => row.SuggestedOrderQty).ToList(),
            ("suggestedorderqty", false) => allRows.OrderBy(row => row.SuggestedOrderQty).ToList(),
            _ => allRows.OrderBy(row => row.CurrentStock).ThenByDescending(row => row.SuggestedOrderQty).ToList()
        };

        var totalCount = allRows.Count;
        if (filter.PageSize > 0)
        {
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        }

        return new LowStockReportReadModel(allRows, totalCount, summary);
    }

    private static LowStockReportReadModel EmptyReport() =>
        new(
            Array.Empty<LowStockRowReadModel>(),
            0,
            new LowStockSummaryReadModel(0, 0, 0, 0));
}
