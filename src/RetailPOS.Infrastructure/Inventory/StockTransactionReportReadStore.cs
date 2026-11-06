using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.InventoryReports;

public sealed class StockTransactionReportReadStore(RetailPOSDbContext dbContext)
    : IStockTransactionReportReadStore
{
    public async Task<StockTransactionReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        StockTransactionReportFilter filter,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == filter.ProductId
                    && (!scope.BusinessId.HasValue || item.BusinessId == scope.BusinessId.Value),
                cancellationToken)
            ?? throw new KeyNotFoundException($"Product {filter.ProductId} not found");

        var variantIds = filter.VariantId.HasValue
            ? new List<long> { filter.VariantId.Value }
            : await dbContext.ProductVariants
                .AsNoTracking()
                .Where(variant => variant.ProductId == filter.ProductId)
                .Select(variant => variant.Id)
                .ToListAsync(cancellationToken);

        ProductVariant? singleVariant = null;
        if (filter.VariantId.HasValue)
        {
            singleVariant = await dbContext.ProductVariants
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    variant => variant.Id == filter.VariantId.Value
                        && variant.ProductId == filter.ProductId,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Variant {filter.VariantId.Value} does not belong to product {filter.ProductId}");
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

        // Inclusive UTC instants: the query handler resolves calendar dates in the Company time zone.
        var rangeStart = filter.DateFrom;
        var rangeEnd = filter.DateTo;
        var baseQuery = dbContext.StockLedgers
            .AsNoTracking()
            .Where(ledger => variantIds.Contains(ledger.VariantId));
        baseQuery = ApplyLocationScope(baseQuery, filter, outletIds, warehouseIds);

        var opening = 0;
        if (rangeStart.HasValue)
        {
            opening = await baseQuery
                .Where(ledger => ledger.CreatedAt < rangeStart.Value)
                .SumAsync(ledger => (int?)(ledger.QtyIn - ledger.QtyOut), cancellationToken) ?? 0;
        }

        var inWindowQuery = baseQuery;
        if (rangeStart.HasValue)
        {
            inWindowQuery = inWindowQuery.Where(ledger => ledger.CreatedAt >= rangeStart.Value);
        }
        if (rangeEnd.HasValue)
        {
            inWindowQuery = inWindowQuery.Where(ledger => ledger.CreatedAt <= rangeEnd.Value);
        }

        var ledgerRows = await inWindowQuery
            .OrderBy(ledger => ledger.CreatedAt)
            .ThenBy(ledger => ledger.Id)
            .Select(ledger => new
            {
                ledger.Id,
                ledger.CreatedAt,
                ledger.LocationId,
                ledger.LocationType,
                ledger.TransactionType,
                ledger.ReferenceType,
                ledger.ReferenceId,
                ledger.VariantId,
                VariantSku = ledger.Variant.Sku,
                VariantAttributes = ledger.Variant.Attributes,
                ledger.QtyIn,
                ledger.QtyOut,
                ledger.Remarks,
                CreatedByName = ledger.Creator != null ? ledger.Creator.Name : null
            })
            .ToListAsync(cancellationToken);

        var ledgerOutletIds = ledgerRows
            .Where(row => row.LocationType == "outlet")
            .Select(row => row.LocationId)
            .Distinct()
            .ToList();
        var ledgerWarehouseIds = ledgerRows
            .Where(row => row.LocationType == "warehouse")
            .Select(row => row.LocationId)
            .Distinct()
            .ToList();
        var outletNames = ledgerOutletIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Outlets
                .AsNoTracking()
                .Where(outlet => ledgerOutletIds.Contains(outlet.Id))
                .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);
        var warehouseNames = ledgerWarehouseIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Warehouses
                .AsNoTracking()
                .Where(warehouse => ledgerWarehouseIds.Contains(warehouse.Id))
                .ToDictionaryAsync(warehouse => warehouse.Id, warehouse => warehouse.Name, cancellationToken);

        var saleIds = ledgerRows
            .Where(row => row.ReferenceType == StockLedgerReferenceType.Sale)
            .Select(row => row.ReferenceId)
            .Distinct()
            .ToList();
        var grnIds = ledgerRows
            .Where(row => row.ReferenceType == StockLedgerReferenceType.Grn)
            .Select(row => row.ReferenceId)
            .Distinct()
            .ToList();
        var transferIds = ledgerRows
            .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer)
            .Select(row => row.ReferenceId)
            .Distinct()
            .ToList();
        var adjustmentIds = ledgerRows
            .Where(row => row.ReferenceType == StockLedgerReferenceType.StockAdjustment)
            .Select(row => row.ReferenceId)
            .Distinct()
            .ToList();
        var saleNumbers = saleIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Sales
                .AsNoTracking()
                .Where(sale => saleIds.Contains(sale.Id) && outletIds.Contains(sale.OutletId))
                .ToDictionaryAsync(sale => sale.Id, sale => sale.SaleNumber, cancellationToken);
        var grnNumbers = grnIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Grns
                .AsNoTracking()
                .Where(grn => grnIds.Contains(grn.Id)
                    && ((grn.PurchaseOrder.WarehouseId != null
                            && warehouseIds.Contains(grn.PurchaseOrder.WarehouseId.Value))
                        || (grn.PurchaseOrder.OutletId != null
                            && outletIds.Contains(grn.PurchaseOrder.OutletId.Value))))
                .Select(grn => new { grn.Id, grn.PoId })
                .ToDictionaryAsync(grn => grn.Id, grn => $"GRN-{grn.Id} (PO-{grn.PoId})", cancellationToken);

        // The ledger rows above are already location-scoped, so their documents need no second scope check.
        var transferNumbers = transferIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.StockTransfers
                .AsNoTracking()
                .Where(transfer => transferIds.Contains(transfer.Id) && transfer.TransferNo != null)
                .ToDictionaryAsync(transfer => transfer.Id, transfer => transfer.TransferNo!, cancellationToken);
        var adjustmentNumbers = adjustmentIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.StockAdjustments
                .AsNoTracking()
                .Where(adjustment => adjustmentIds.Contains(adjustment.Id))
                .ToDictionaryAsync(adjustment => adjustment.Id, adjustment => adjustment.AdjustmentNumber, cancellationToken);

        var rows = new List<StockTransactionReportRowReadModel>(ledgerRows.Count);
        var running = opening;
        var totalIn = 0;
        var totalOut = 0;
        foreach (var row in ledgerRows)
        {
            running += row.QtyIn - row.QtyOut;
            totalIn += row.QtyIn;
            totalOut += row.QtyOut;

            var locationName = row.LocationType == "outlet"
                ? outletNames.GetValueOrDefault(row.LocationId, $"Outlet {row.LocationId}")
                : row.LocationType == "warehouse"
                    ? warehouseNames.GetValueOrDefault(row.LocationId, $"Warehouse {row.LocationId}")
                    : $"Location {row.LocationId}";
            var referenceNumber = row.ReferenceType switch
            {
                StockLedgerReferenceType.Sale => saleNumbers.GetValueOrDefault(
                    row.ReferenceId,
                    $"SALE-{row.ReferenceId}"),
                StockLedgerReferenceType.Grn => grnNumbers.GetValueOrDefault(
                    row.ReferenceId,
                    $"GRN-{row.ReferenceId}"),
                StockLedgerReferenceType.StockTransfer => transferNumbers.GetValueOrDefault(
                    row.ReferenceId,
                    $"TRF-{row.ReferenceId}"),
                StockLedgerReferenceType.StockAdjustment => adjustmentNumbers.GetValueOrDefault(
                    row.ReferenceId,
                    $"ADJ-{row.ReferenceId}"),
                _ => $"{row.ReferenceType}-{row.ReferenceId}"
            };

            rows.Add(new StockTransactionReportRowReadModel(
                row.Id,
                row.CreatedAt,
                row.LocationId,
                row.LocationType,
                locationName,
                row.TransactionType,
                row.ReferenceType,
                row.ReferenceId,
                referenceNumber,
                row.VariantId,
                row.VariantSku,
                row.VariantAttributes,
                row.QtyIn,
                row.QtyOut,
                running,
                row.Remarks,
                row.CreatedByName));
        }

        var currentStockQuery = dbContext.Inventories
            .AsNoTracking()
            .Where(inventory => variantIds.Contains(inventory.VariantId));
        currentStockQuery = ApplyInventoryLocationScope(
            currentStockQuery,
            filter,
            outletIds,
            warehouseIds);
        var currentStock = await currentStockQuery
            .SumAsync(inventory => (int?)inventory.Quantity, cancellationToken) ?? 0;

        return new StockTransactionReportReadModel(
            product.Id,
            product.Name,
            product.ProductCode ?? string.Empty,
            singleVariant is null ? "product" : "variant",
            singleVariant?.Id,
            singleVariant?.Sku,
            singleVariant?.Name,
            singleVariant?.Attributes,
            singleVariant?.Sku ?? product.Sku ?? string.Empty,
            filter.LocationId,
            filter.LocationType,
            filter.DateFrom,
            filter.DateTo,
            new StockTransactionReportSummaryReadModel(
                opening,
                totalIn,
                totalOut,
                opening + totalIn - totalOut,
                currentStock),
            rows);
    }

    private static IQueryable<StockLedger> ApplyLocationScope(
        IQueryable<StockLedger> query,
        StockTransactionReportFilter filter,
        IReadOnlyCollection<long> outletIds,
        IReadOnlyCollection<long> warehouseIds)
    {
        if (filter.LocationId.HasValue)
        {
            var allowed = filter.LocationType switch
            {
                "outlet" => outletIds.Contains(filter.LocationId.Value),
                "warehouse" => warehouseIds.Contains(filter.LocationId.Value),
                _ => outletIds.Contains(filter.LocationId.Value)
                    || warehouseIds.Contains(filter.LocationId.Value)
            };
            if (!allowed)
            {
                return query.Where(_ => false);
            }

            if (string.IsNullOrWhiteSpace(filter.LocationType))
            {
                var allowedOutlets = outletIds.ToList();
                var allowedWarehouses = warehouseIds.ToList();
                query = query.Where(ledger =>
                    ledger.LocationId == filter.LocationId.Value
                    && (ledger.LocationType == "outlet" && allowedOutlets.Contains(ledger.LocationId)
                        || ledger.LocationType == "warehouse" && allowedWarehouses.Contains(ledger.LocationId)));
            }
            else
            {
                query = query.Where(ledger =>
                    ledger.LocationId == filter.LocationId.Value
                    && ledger.LocationType == filter.LocationType);
            }
        }
        else
        {
            var allowedOutlets = outletIds.ToList();
            var allowedWarehouses = warehouseIds.ToList();
            query = query.Where(ledger =>
                ledger.LocationType == "outlet" && allowedOutlets.Contains(ledger.LocationId)
                || ledger.LocationType == "warehouse" && allowedWarehouses.Contains(ledger.LocationId));
        }

        if (!string.IsNullOrWhiteSpace(filter.LocationType))
        {
            query = query.Where(ledger => ledger.LocationType == filter.LocationType);
        }

        return query;
    }

    private static IQueryable<RetailPOS.Core.Entities.Inventory> ApplyInventoryLocationScope(
        IQueryable<RetailPOS.Core.Entities.Inventory> query,
        StockTransactionReportFilter filter,
        IReadOnlyCollection<long> outletIds,
        IReadOnlyCollection<long> warehouseIds)
    {
        if (filter.LocationId.HasValue)
        {
            var allowed = filter.LocationType switch
            {
                "outlet" => outletIds.Contains(filter.LocationId.Value),
                "warehouse" => warehouseIds.Contains(filter.LocationId.Value),
                _ => outletIds.Contains(filter.LocationId.Value)
                    || warehouseIds.Contains(filter.LocationId.Value)
            };
            if (!allowed)
            {
                return query.Where(_ => false);
            }

            if (string.IsNullOrWhiteSpace(filter.LocationType))
            {
                var allowedOutlets = outletIds.ToList();
                var allowedWarehouses = warehouseIds.ToList();
                query = query.Where(inventory =>
                    inventory.LocationId == filter.LocationId.Value
                    && (inventory.LocationType == "outlet" && allowedOutlets.Contains(inventory.LocationId)
                        || inventory.LocationType == "warehouse" && allowedWarehouses.Contains(inventory.LocationId)));
            }
            else
            {
                query = query.Where(inventory =>
                    inventory.LocationId == filter.LocationId.Value
                    && inventory.LocationType == filter.LocationType);
            }
        }
        else
        {
            var allowedOutlets = outletIds.ToList();
            var allowedWarehouses = warehouseIds.ToList();
            query = query.Where(inventory =>
                inventory.LocationType == "outlet" && allowedOutlets.Contains(inventory.LocationId)
                || inventory.LocationType == "warehouse" && allowedWarehouses.Contains(inventory.LocationId));
        }

        if (!string.IsNullOrWhiteSpace(filter.LocationType))
        {
            query = query.Where(inventory => inventory.LocationType == filter.LocationType);
        }

        return query;
    }
}
