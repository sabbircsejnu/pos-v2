using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.InventoryReports;

public sealed class OutletWiseStockReportReadStore(RetailPOSDbContext dbContext)
    : IOutletWiseStockReportReadStore
{
    public async Task<OutletWiseStockReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        OutletWiseStockFilter filter,
        CancellationToken cancellationToken)
    {
        var outletIds = scope.OutletIds.ToList();
        if (scope.BusinessId.HasValue)
        {
            var businessId = scope.BusinessId.Value;
            outletIds = await dbContext.Outlets
                .AsNoTracking()
                .Where(outlet => outlet.BusinessId == businessId && outletIds.Contains(outlet.Id))
                .Select(outlet => outlet.Id)
                .ToListAsync(cancellationToken);
        }

        if (filter.OutletId.HasValue && !outletIds.Contains(filter.OutletId.Value))
        {
            return EmptyReport();
        }

        var inventoryQuery = dbContext.Inventories
            .AsNoTracking()
            .Where(inventory =>
                inventory.LocationType == "outlet"
                && (filter.OutletId.HasValue
                    ? inventory.LocationId == filter.OutletId.Value
                    : outletIds.Contains(inventory.LocationId)));

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

        var outletNames = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outletIds.Contains(outlet.Id))
            .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);

        var allRows = inventoryRows.Select(row =>
        {
            var unitCost = row.CostPrice + row.CostAdjustment;
            return new OutletWiseStockRowReadModel(
                row.LocationId,
                outletNames.GetValueOrDefault(row.LocationId, $"Outlet {row.LocationId}"),
                row.VariantId,
                row.ProductCode,
                row.Barcode,
                row.Sku,
                row.ProductName,
                row.CategoryName,
                row.Quantity,
                unitCost,
                unitCost * row.Quantity);
        }).ToList();

        var nonPcsVariants = await RetailPOS.Infrastructure.Uom.UomReportTotals.NonPcsVariantIdsAsync(dbContext, cancellationToken);
        var byOutlet = allRows
            .GroupBy(row => row.OutletId)
            .Select(group => new OutletStockSummaryReadModel(
                group.Key,
                group.First().OutletName,
                group.Select(row => row.VariantId).Distinct().Count(),
                group.Where(row => !nonPcsVariants.Contains(row.VariantId)).Sum(row => row.Quantity),
                group.Sum(row => row.StockValue)))
            .OrderBy(outlet => outlet.OutletName)
            .ToArray();
        var summary = new OutletWiseStockSummaryReadModel(
            byOutlet.Length,
            allRows.Select(row => row.VariantId).Distinct().Count(),
            allRows.Where(row => !nonPcsVariants.Contains(row.VariantId)).Sum(row => row.Quantity),
            allRows.Sum(row => row.StockValue),
            byOutlet);

        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("outletname", true) => allRows.OrderByDescending(row => row.OutletName).ToList(),
            ("outletname", false) => allRows.OrderBy(row => row.OutletName).ThenBy(row => row.ProductName).ToList(),
            ("productname", true) => allRows.OrderByDescending(row => row.ProductName).ToList(),
            ("productname", false) => allRows.OrderBy(row => row.ProductName).ToList(),
            ("sku", true) => allRows.OrderByDescending(row => row.Sku).ToList(),
            ("sku", false) => allRows.OrderBy(row => row.Sku).ToList(),
            ("category", true) => allRows.OrderByDescending(row => row.CategoryName).ToList(),
            ("category", false) => allRows.OrderBy(row => row.CategoryName).ToList(),
            ("quantity", true) => allRows.OrderByDescending(row => row.Quantity).ToList(),
            ("quantity", false) => allRows.OrderBy(row => row.Quantity).ToList(),
            ("stockvalue", true) => allRows.OrderByDescending(row => row.StockValue).ToList(),
            ("stockvalue", false) => allRows.OrderBy(row => row.StockValue).ToList(),
            _ => allRows.OrderBy(row => row.OutletName).ThenBy(row => row.ProductName).ToList()
        };

        var totalCount = allRows.Count;
        if (filter.PageSize > 0)
        {
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        }

        return new OutletWiseStockReportReadModel(allRows, totalCount, summary);
    }

    private static OutletWiseStockReportReadModel EmptyReport() =>
        new(
            Array.Empty<OutletWiseStockRowReadModel>(),
            0,
            new OutletWiseStockSummaryReadModel(0, 0, 0, 0m, Array.Empty<OutletStockSummaryReadModel>()));
}
