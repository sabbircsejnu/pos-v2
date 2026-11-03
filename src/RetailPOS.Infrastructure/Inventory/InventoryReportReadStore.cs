using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.InventoryReports;

public sealed class InventoryReportReadStore(RetailPOSDbContext dbContext)
    : IInventoryReportReadStore
{
    public async Task<IReadOnlyList<StockLevelReadModel>> GetStockLevelsAsync(
        InventoryReportReadScope scope,
        long? locationId,
        string? locationType,
        bool lowStockOnly,
        CancellationToken cancellationToken)
    {
        var query = await ApplyScopeAsync(dbContext.Inventories
            .AsNoTracking()
            .Include(inventory => inventory.Variant)
                .ThenInclude(variant => variant.Product)
                    .ThenInclude(product => product.Category), scope, cancellationToken);

        if (locationId.HasValue)
        {
            query = query.Where(inventory => inventory.LocationId == locationId.Value);
        }
        if (!string.IsNullOrWhiteSpace(locationType))
        {
            query = query.Where(inventory => inventory.LocationType == locationType);
        }
        if (lowStockOnly)
        {
            query = query.Where(inventory => inventory.Quantity <= inventory.LowStockThreshold);
        }

        List<RetailPOS.Core.Entities.Inventory> inventories;
        inventories = await query.ToListAsync(cancellationToken);
        var outletIds = inventories
            .Where(inventory => inventory.LocationType == "outlet")
            .Select(inventory => inventory.LocationId)
            .Distinct()
            .ToList();
        var warehouseIds = inventories
            .Where(inventory => inventory.LocationType == "warehouse")
            .Select(inventory => inventory.LocationId)
            .Distinct()
            .ToList();

        var outletNames = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outletIds.Contains(outlet.Id))
            .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);
        var warehouseNames = await dbContext.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouseIds.Contains(warehouse.Id))
            .ToDictionaryAsync(warehouse => warehouse.Id, warehouse => warehouse.Name, cancellationToken);

        return inventories.Select(inventory =>
        {
            var locationName = inventory.LocationType == "outlet"
                ? outletNames.GetValueOrDefault(inventory.LocationId, string.Empty)
                : warehouseNames.GetValueOrDefault(inventory.LocationId, string.Empty);
            var costPrice = inventory.Variant.Product.CostPrice + inventory.Variant.CostAdjustment;
            var isLowStock = inventory.Quantity <= inventory.LowStockThreshold;

            return new StockLevelReadModel(
                inventory.VariantId,
                inventory.Variant.Product.Name,
                inventory.Variant.Sku,
                inventory.Variant.Product.Category?.Name ?? "Uncategorized",
                inventory.LocationId,
                inventory.LocationType,
                locationName,
                inventory.Quantity,
                inventory.LowStockThreshold,
                isLowStock,
                costPrice * inventory.Quantity);
        }).ToArray();
    }

    public async Task<InventoryReportValuationReadModel> GetValuationAsync(
        InventoryReportReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = await ApplyScopeAsync(dbContext.Inventories
                .AsNoTracking()
                .Include(inventory => inventory.Variant)
                    .ThenInclude(variant => variant.Product)
                        .ThenInclude(product => product.Category),
                scope,
                cancellationToken);
        var inventories = await query.ToListAsync(cancellationToken);

        var byCategory = inventories
            .GroupBy(inventory => inventory.Variant.Product.Category?.Name ?? "Uncategorized")
            .Select(group => new InventoryReportCategoryValuationReadModel(
                group.Key,
                group.Sum(inventory =>
                    (inventory.Variant.Product.CostPrice + inventory.Variant.CostAdjustment) * inventory.Quantity),
                group.Sum(inventory => inventory.Quantity)))
            .OrderByDescending(category => category.Value)
            .ToArray();

        return new InventoryReportValuationReadModel(
            byCategory.Sum(category => category.Value),
            byCategory.Sum(category => category.ItemCount),
            byCategory);
    }

    public async Task<IReadOnlyList<SlowMovingItemReadModel>> GetSlowMovingItemsAsync(
        InventoryReportReadScope scope,
        int days,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var outletIds = scope.OutletIds.ToList();
        var inventoryQuery = await ApplyScopeAsync(dbContext.Inventories
                .AsNoTracking()
                .Include(inventory => inventory.Variant)
                    .ThenInclude(variant => variant.Product),
                scope,
                cancellationToken);
        var inventories = await inventoryQuery.ToListAsync(cancellationToken);
        var activeSaleVariantIds = await ScopedSaleItems(scope, outletIds)
            .Where(saleItem => saleItem.Sale.SaleDate >= cutoff)
            .Select(saleItem => saleItem.VariantId)
            .Distinct()
            .ToListAsync(cancellationToken);
        inventories = inventories
            .Where(inventory => !activeSaleVariantIds.Contains(inventory.VariantId))
            .ToList();

        var variantIds = inventories.Select(inventory => inventory.VariantId).Distinct().ToList();
        var lastSaleDates = await ScopedSaleItems(scope, outletIds)
            .Where(saleItem => variantIds.Contains(saleItem.VariantId))
            .GroupBy(saleItem => saleItem.VariantId)
            .Select(group => new { VariantId = group.Key, LastSale = group.Max(saleItem => saleItem.Sale.SaleDate) })
            .ToDictionaryAsync(result => result.VariantId, result => result.LastSale, cancellationToken);

        var now = DateTime.UtcNow;
        return inventories
            .GroupBy(inventory => inventory.VariantId)
            .Select(group =>
            {
                var first = group.First();
                var costPrice = first.Variant.Product.CostPrice + first.Variant.CostAdjustment;
                var totalStock = group.Sum(inventory => inventory.Quantity);
                var daysSince = lastSaleDates.TryGetValue(first.VariantId, out var lastSale)
                    ? (int)(now - lastSale).TotalDays
                    : int.MaxValue;

                return new SlowMovingItemReadModel(
                    first.VariantId,
                    first.Variant.Product.Name,
                    first.Variant.Sku,
                    totalStock,
                    daysSince,
                    costPrice * totalStock);
            })
            .OrderByDescending(item => item.DaysSinceLastSale)
            .ToArray();
    }

    public async Task<CurrentStockReportReadModel> GetCurrentStockAsync(
        InventoryReportReadScope scope,
        CurrentStockReportFilter filter,
        CancellationToken cancellationToken)
    {
        var query = await ApplyScopeAsync(
            dbContext.Inventories
                .AsNoTracking()
                .Include(inventory => inventory.Variant)
                    .ThenInclude(variant => variant.Product)
                        .ThenInclude(product => product.Category),
            scope,
            cancellationToken);

        if (filter.OutletId.HasValue)
        {
            query = query.Where(inventory =>
                inventory.LocationType == "outlet" && inventory.LocationId == filter.OutletId.Value);
        }
        else if (filter.WarehouseId.HasValue)
        {
            query = query.Where(inventory =>
                inventory.LocationType == "warehouse" && inventory.LocationId == filter.WarehouseId.Value);
        }

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(inventory =>
                inventory.Variant.Product.CategoryId == filter.CategoryId.Value);
        }
        if (filter.ProductId.HasValue)
        {
            query = query.Where(inventory => inventory.Variant.ProductId == filter.ProductId.Value);
        }

        query = filter.StockStatus switch
        {
            "out-of-stock" => query.Where(inventory => inventory.Quantity == 0),
            "low-stock" => query.Where(inventory =>
                inventory.Quantity > 0 && inventory.Quantity <= inventory.LowStockThreshold),
            "in-stock" => query.Where(inventory => inventory.Quantity > inventory.LowStockThreshold),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(inventory =>
                inventory.Variant.Sku.ToLower().Contains(search)
                || (inventory.Variant.Barcode != null && inventory.Variant.Barcode.ToLower().Contains(search))
                || inventory.Variant.Product.Name.ToLower().Contains(search)
                || (inventory.Variant.Product.ProductCode != null
                    && inventory.Variant.Product.ProductCode.ToLower().Contains(search))
                || (inventory.Variant.Product.Barcode != null
                    && inventory.Variant.Product.Barcode.ToLower().Contains(search)));
        }

        var inventories = await query.ToListAsync(cancellationToken);
        if (inventories.Count == 0)
        {
            return new CurrentStockReportReadModel(
                Array.Empty<CurrentStockRowReadModel>(),
                0,
                new CurrentStockSummaryReadModel(0, 0, 0m));
        }

        var outletIds = inventories
            .Where(inventory => inventory.LocationType == "outlet")
            .Select(inventory => inventory.LocationId)
            .Distinct()
            .ToList();
        var warehouseIds = inventories
            .Where(inventory => inventory.LocationType == "warehouse")
            .Select(inventory => inventory.LocationId)
            .Distinct()
            .ToList();

        var outletNames = outletIds.Count > 0
            ? await dbContext.Outlets.AsNoTracking()
                .Where(outlet => outletIds.Contains(outlet.Id))
                .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken)
            : new Dictionary<long, string>();
        var warehouseNames = warehouseIds.Count > 0
            ? await dbContext.Warehouses.AsNoTracking()
                .Where(warehouse => warehouseIds.Contains(warehouse.Id))
                .ToDictionaryAsync(warehouse => warehouse.Id, warehouse => warehouse.Name, cancellationToken)
            : new Dictionary<long, string>();

        var variantIds = inventories.Select(inventory => inventory.VariantId).Distinct().ToList();
        var purchaseWarehouseIds = scope.WarehouseIds.ToList();
        if (filter.WarehouseId.HasValue)
        {
            purchaseWarehouseIds = purchaseWarehouseIds
                .Where(id => id == filter.WarehouseId.Value)
                .ToList();
        }

        // Purchases may be received directly into outlets as well as warehouses.
        var purchaseOutletIds = filter.WarehouseId.HasValue && !filter.OutletId.HasValue
            ? new List<long>()
            : scope.OutletIds
                .Where(id => !filter.OutletId.HasValue || id == filter.OutletId.Value)
                .ToList();

        var lastPurchaseDates = purchaseWarehouseIds.Count == 0 && purchaseOutletIds.Count == 0
            ? new Dictionary<long, DateTime>()
            : await (
                from grnItem in dbContext.GrnItems.AsNoTracking()
                join grn in dbContext.Grns.AsNoTracking() on grnItem.GrnId equals grn.Id
                join purchaseOrderItem in dbContext.PurchaseOrderItems.AsNoTracking()
                    on grnItem.PoItemId equals purchaseOrderItem.Id
                join purchaseOrder in dbContext.PurchaseOrders.AsNoTracking()
                    on grn.PoId equals purchaseOrder.Id
                where variantIds.Contains(purchaseOrderItem.VariantId)
                    && ((purchaseOrder.WarehouseId != null
                            && purchaseWarehouseIds.Contains(purchaseOrder.WarehouseId.Value))
                        || (purchaseOrder.OutletId != null
                            && purchaseOutletIds.Contains(purchaseOrder.OutletId.Value)))
                group grn.ReceivedDate by purchaseOrderItem.VariantId into grouped
                select new { VariantId = grouped.Key, LastDate = grouped.Max() })
                .ToDictionaryAsync(item => item.VariantId, item => item.LastDate, cancellationToken);

        var saleOutletIds = scope.OutletIds.ToList();
        if (filter.OutletId.HasValue)
        {
            saleOutletIds = saleOutletIds.Where(id => id == filter.OutletId.Value).ToList();
        }

        var lastSaleDates = saleOutletIds.Count == 0
            ? new Dictionary<long, DateTime>()
            : await ScopedSaleItems(scope, saleOutletIds)
                .Where(saleItem => variantIds.Contains(saleItem.VariantId))
                .GroupBy(saleItem => saleItem.VariantId)
                .Select(group => new
                {
                    VariantId = group.Key,
                    LastDate = group.Max(saleItem => saleItem.Sale.SaleDate)
                })
                .ToDictionaryAsync(item => item.VariantId, item => item.LastDate, cancellationToken);

        var rows = inventories.Select(inventory =>
        {
            var locationName = inventory.LocationType == "outlet"
                ? outletNames.GetValueOrDefault(inventory.LocationId, $"Outlet {inventory.LocationId}")
                : warehouseNames.GetValueOrDefault(inventory.LocationId, $"Warehouse {inventory.LocationId}");
            var unitCost = inventory.Variant.Product.CostPrice + inventory.Variant.CostAdjustment;
            var stockStatus = inventory.Quantity == 0 ? "out-of-stock"
                : inventory.Quantity <= inventory.LowStockThreshold ? "low-stock"
                : "in-stock";

            lastPurchaseDates.TryGetValue(inventory.VariantId, out var lastPurchase);
            lastSaleDates.TryGetValue(inventory.VariantId, out var lastSale);

            return new CurrentStockRowReadModel(
                inventory.VariantId,
                inventory.Variant.Product.ProductCode ?? string.Empty,
                inventory.Variant.Barcode ?? inventory.Variant.Product.Barcode,
                inventory.Variant.Product.Name,
                inventory.Variant.Product.Category?.Name ?? "Uncategorized",
                inventory.LocationId,
                inventory.LocationType,
                locationName,
                inventory.Quantity,
                ReservedQuantity: 0,
                inventory.LowStockThreshold,
                unitCost,
                unitCost * inventory.Quantity,
                lastPurchase == default ? null : lastPurchase,
                lastSale == default ? null : lastSale,
                stockStatus);
        }).ToList();

        rows = (filter.SortBy?.ToLower(), filter.SortDir?.ToLower()) switch
        {
            ("productcode", "desc") => rows.OrderByDescending(row => row.ProductCode).ToList(),
            ("productcode", _) => rows.OrderBy(row => row.ProductCode).ToList(),
            ("barcode", "desc") => rows.OrderByDescending(row => row.Barcode).ToList(),
            ("barcode", _) => rows.OrderBy(row => row.Barcode).ToList(),
            ("category", "desc") => rows.OrderByDescending(row => row.Category).ToList(),
            ("category", _) => rows.OrderBy(row => row.Category).ToList(),
            ("location", "desc") => rows.OrderByDescending(row => row.LocationName).ToList(),
            ("location", _) => rows.OrderBy(row => row.LocationName).ToList(),
            ("availablequantity", "desc") => rows.OrderByDescending(row => row.AvailableQuantity).ToList(),
            ("availablequantity", _) => rows.OrderBy(row => row.AvailableQuantity).ToList(),
            ("stockvalue", "desc") => rows.OrderByDescending(row => row.StockValue).ToList(),
            ("stockvalue", _) => rows.OrderBy(row => row.StockValue).ToList(),
            ("unitcost", "desc") => rows.OrderByDescending(row => row.UnitCost).ToList(),
            ("unitcost", _) => rows.OrderBy(row => row.UnitCost).ToList(),
            _ => rows.OrderBy(row => row.ProductName).ToList()
        };

        var summary = new CurrentStockSummaryReadModel(
            rows.Count,
            rows.Sum(row => row.AvailableQuantity),
            rows.Sum(row => row.StockValue));
        var pageRows = filter.PageSize > 0
            ? rows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToArray()
            : rows.ToArray();

        return new CurrentStockReportReadModel(pageRows, summary.TotalProducts, summary);
    }

    private async Task<IQueryable<RetailPOS.Core.Entities.Inventory>> ApplyScopeAsync(
        IQueryable<RetailPOS.Core.Entities.Inventory> query,
        InventoryReportReadScope scope,
        CancellationToken cancellationToken)
    {
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

        return query.Where(inventory =>
            inventory.LocationType == "outlet" && outletIds.Contains(inventory.LocationId)
            || inventory.LocationType == "warehouse" && warehouseIds.Contains(inventory.LocationId));
    }

    private IQueryable<RetailPOS.Core.Entities.SaleItem> ScopedSaleItems(
        InventoryReportReadScope scope,
        IReadOnlyCollection<long> outletIds)
    {
        var query = dbContext.SaleItems
            .AsNoTracking()
            .Where(saleItem => outletIds.Contains(saleItem.Sale.OutletId));
        if (scope.BusinessId.HasValue)
        {
            var businessId = scope.BusinessId.Value;
            query = query.Where(saleItem => saleItem.Sale.Outlet.BusinessId == businessId);
        }

        return query;
    }
}
