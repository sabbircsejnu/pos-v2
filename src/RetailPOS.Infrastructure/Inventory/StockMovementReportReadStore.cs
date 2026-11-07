using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.InventoryReports;

public sealed class StockMovementReportReadStore(RetailPOSDbContext dbContext)
    : IStockMovementReportReadStore
{
    public async Task<StockMovementReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        StockMovementReportFilter filter,
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

        // Inclusive UTC instants: the query handler resolves calendar dates in the Company time zone.
        var rangeEnd = filter.DateTo;
        // A cost correction moves no stock, so it must not list a product that did not move.
        var ledgerQuery = dbContext.StockLedgers
            .AsNoTracking()
            .Where(ledger => ledger.TransactionType != StockLedgerTransactionType.CostCorrection);
        if (filter.DateFrom.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(ledger => ledger.CreatedAt >= filter.DateFrom.Value);
        }
        if (rangeEnd.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(ledger => ledger.CreatedAt <= rangeEnd.Value);
        }

        if (filter.OutletId.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(ledger =>
                ledger.LocationType == "outlet" && ledger.LocationId == filter.OutletId.Value);
        }
        else if (filter.WarehouseId.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(ledger =>
                ledger.LocationType == "warehouse" && ledger.LocationId == filter.WarehouseId.Value);
        }
        else
        {
            var allowedOutlets = outletIds.ToList();
            var allowedWarehouses = warehouseIds.ToList();
            ledgerQuery = ledgerQuery.Where(ledger =>
                ledger.LocationType == "outlet" && allowedOutlets.Contains(ledger.LocationId)
                || ledger.LocationType == "warehouse" && allowedWarehouses.Contains(ledger.LocationId));
        }

        if (filter.CategoryId.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(ledger =>
                ledger.Variant.Product.CategoryId == filter.CategoryId.Value);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            ledgerQuery = ledgerQuery.Where(ledger =>
                ledger.Variant.Sku.ToLower().Contains(search)
                || ledger.Variant.Product.Name.ToLower().Contains(search)
                || ledger.Variant.Product.ProductCode != null
                    && ledger.Variant.Product.ProductCode.ToLower().Contains(search));
        }

        var aggregates = await ledgerQuery
            .GroupBy(ledger => new
            {
                ledger.VariantId,
                ledger.LocationId,
                ledger.LocationType
            })
            .Select(group => new
            {
                group.Key.VariantId,
                group.Key.LocationId,
                group.Key.LocationType,
                StockIn = group.Sum(ledger => ledger.QtyIn),
                StockOut = group.Sum(ledger => ledger.QtyOut)
            })
            .ToListAsync(cancellationToken);
        if (aggregates.Count == 0)
        {
            return EmptyReport();
        }

        var variantIds = aggregates.Select(row => row.VariantId).Distinct().ToList();
        var outletLocations = aggregates
            .Where(row => row.LocationType == "outlet")
            .Select(row => row.LocationId)
            .Distinct()
            .ToList();
        var warehouseLocations = aggregates
            .Where(row => row.LocationType == "warehouse")
            .Select(row => row.LocationId)
            .Distinct()
            .ToList();
        var variantInfo = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id))
            .Select(variant => new
            {
                variant.Id,
                variant.Sku,
                ProductName = variant.Product.Name,
                VariantName = variant.Name,
                VariantAttributes = variant.Attributes,
                ProductCode = variant.Product.ProductCode ?? string.Empty,
                CategoryName = variant.Product.Category != null
                    ? variant.Product.Category.Name
                    : "Uncategorized"
            })
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        var openingBalances = new Dictionary<(long VariantId, long LocationId, string LocationType), decimal>();
        if (filter.DateFrom.HasValue)
        {
            var priorRows = await dbContext.StockLedgers
                .AsNoTracking()
                .Where(ledger =>
                    variantIds.Contains(ledger.VariantId)
                    && ledger.CreatedAt < filter.DateFrom.Value
                    && (ledger.LocationType == "outlet" && outletLocations.Contains(ledger.LocationId)
                        || ledger.LocationType == "warehouse"
                            && warehouseLocations.Contains(ledger.LocationId)))
                .Select(ledger => new
                {
                    ledger.VariantId,
                    ledger.LocationId,
                    ledger.LocationType,
                    ledger.BalanceAfter,
                    ledger.Id
                })
                .ToListAsync(cancellationToken);
            openingBalances = priorRows
                .GroupBy(row => (row.VariantId, row.LocationId, row.LocationType))
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(row => row.Id).First().BalanceAfter);
        }

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

        var allRows = aggregates.Select(aggregate =>
        {
            var opening = openingBalances.GetValueOrDefault(
                (aggregate.VariantId, aggregate.LocationId, aggregate.LocationType),
                0);
            var closing = opening + aggregate.StockIn - aggregate.StockOut;
            var info = variantInfo.GetValueOrDefault(aggregate.VariantId);
            var locationName = aggregate.LocationType == "outlet"
                ? outletNames.GetValueOrDefault(aggregate.LocationId, $"Outlet {aggregate.LocationId}")
                : warehouseNames.GetValueOrDefault(
                    aggregate.LocationId,
                    $"Warehouse {aggregate.LocationId}");

            return new StockMovementRowReadModel(
                aggregate.VariantId,
                info?.ProductCode ?? string.Empty,
                info?.Sku ?? string.Empty,
                info?.ProductName ?? string.Empty,
                info?.VariantName ?? string.Empty,
                info?.VariantAttributes,
                info?.CategoryName ?? string.Empty,
                aggregate.LocationId,
                aggregate.LocationType,
                locationName,
                opening,
                aggregate.StockIn,
                aggregate.StockOut,
                closing,
                closing - opening);
        }).ToList();

        allRows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("sku", true) => allRows.OrderByDescending(row => row.Sku).ToList(),
            ("sku", false) => allRows.OrderBy(row => row.Sku).ToList(),
            ("category", true) => allRows.OrderByDescending(row => row.CategoryName).ToList(),
            ("category", false) => allRows.OrderBy(row => row.CategoryName).ToList(),
            ("location", true) => allRows.OrderByDescending(row => row.LocationName).ToList(),
            ("location", false) => allRows.OrderBy(row => row.LocationName).ToList(),
            ("opening", true) => allRows.OrderByDescending(row => row.OpeningStock).ToList(),
            ("opening", false) => allRows.OrderBy(row => row.OpeningStock).ToList(),
            ("stockin", true) => allRows.OrderByDescending(row => row.StockIn).ToList(),
            ("stockin", false) => allRows.OrderBy(row => row.StockIn).ToList(),
            ("stockout", true) => allRows.OrderByDescending(row => row.StockOut).ToList(),
            ("stockout", false) => allRows.OrderBy(row => row.StockOut).ToList(),
            ("closing", true) => allRows.OrderByDescending(row => row.ClosingStock).ToList(),
            ("closing", false) => allRows.OrderBy(row => row.ClosingStock).ToList(),
            ("netmovement", true) => allRows.OrderByDescending(row => row.NetMovement).ToList(),
            ("netmovement", false) => allRows.OrderBy(row => row.NetMovement).ToList(),
            _ => allRows.OrderBy(row => row.ProductName).ThenBy(row => row.Sku).ToList()
        };

        var nonPcsVariants = await RetailPOS.Infrastructure.Uom.UomReportTotals.NonPcsVariantIdsAsync(dbContext, cancellationToken);
        var summary = new StockMovementSummaryReadModel(
            allRows.Where(row => !nonPcsVariants.Contains(row.VariantId)).Sum(row => row.OpeningStock),
            allRows.Where(row => !nonPcsVariants.Contains(row.VariantId)).Sum(row => row.StockIn),
            allRows.Where(row => !nonPcsVariants.Contains(row.VariantId)).Sum(row => row.StockOut),
            allRows.Where(row => !nonPcsVariants.Contains(row.VariantId)).Sum(row => row.ClosingStock),
            allRows.Where(row => !nonPcsVariants.Contains(row.VariantId)).Sum(row => row.NetMovement));
        var totalCount = allRows.Count;
        if (filter.PageSize > 0)
        {
            allRows = allRows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        }

        return new StockMovementReportReadModel(allRows, totalCount, summary);
    }

    private static StockMovementReportReadModel EmptyReport() =>
        new(
            Array.Empty<StockMovementRowReadModel>(),
            0,
            new StockMovementSummaryReadModel(0, 0, 0, 0, 0));
}
