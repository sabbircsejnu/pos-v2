using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.InventoryReports;

public sealed class ProductLedgerReportReadStore(RetailPOSDbContext dbContext)
    : IProductLedgerReportReadStore
{
    public async Task<ProductLedgerReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        ProductLedgerReportFilter filter,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == filter.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"Product {filter.ProductId} not found.");

        string? variantName = null;
        if (filter.VariantId.HasValue)
        {
            variantName = await dbContext.ProductVariants
                .AsNoTracking()
                .Where(variant =>
                    variant.Id == filter.VariantId.Value && variant.ProductId == filter.ProductId)
                .Select(variant => variant.Name)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Variant {filter.VariantId} does not belong to product {filter.ProductId}.");
        }

        var variantIds = filter.VariantId.HasValue
            ? new List<long> { filter.VariantId.Value }
            : await dbContext.ProductVariants
                .AsNoTracking()
                .Where(variant => variant.ProductId == filter.ProductId)
                .Select(variant => variant.Id)
                .ToListAsync(cancellationToken);

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

        var rangeStart = filter.DateFrom;
        var rangeEnd = filter.DateTo.HasValue
            ? filter.DateTo.Value.Date.AddDays(1).AddTicks(-1)
            : (DateTime?)null;

        var opening = 0;
        if (rangeStart.HasValue)
        {
            var openingQuery = dbContext.StockLedgers
                .AsNoTracking()
                .Where(ledger => variantIds.Contains(ledger.VariantId));
            openingQuery = ApplyLocationScope(openingQuery, filter, outletIds, warehouseIds);
            opening = await openingQuery
                .Where(ledger => ledger.CreatedAt < rangeStart.Value)
                .SumAsync(ledger => (int?)(ledger.QtyIn - ledger.QtyOut), cancellationToken) ?? 0;
        }

        var ledgerQuery = dbContext.StockLedgers
            .AsNoTracking()
            .Include(ledger => ledger.Variant)
                .ThenInclude(variant => variant.Product)
            .Include(ledger => ledger.Creator)
            .Where(ledger => variantIds.Contains(ledger.VariantId));
        ledgerQuery = ApplyLocationScope(ledgerQuery, filter, outletIds, warehouseIds);

        if (!string.IsNullOrWhiteSpace(filter.TransactionType))
        {
            ledgerQuery = ledgerQuery.Where(ledger => ledger.TransactionType == filter.TransactionType);
        }
        if (rangeStart.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(ledger => ledger.CreatedAt >= rangeStart.Value);
        }
        if (rangeEnd.HasValue)
        {
            ledgerQuery = ledgerQuery.Where(ledger => ledger.CreatedAt <= rangeEnd.Value);
        }

        var ledgerRows = await ledgerQuery
            .OrderBy(ledger => ledger.CreatedAt)
            .ThenBy(ledger => ledger.Id)
            .Select(ledger => new
            {
                ledger.Id,
                ledger.CreatedAt,
                ledger.LocationId,
                ledger.LocationType,
                ledger.VariantId,
                ledger.TransactionType,
                ledger.ReferenceType,
                ledger.ReferenceId,
                ledger.QtyIn,
                ledger.QtyOut,
                ledger.BalanceAfter,
                ledger.Remarks,
                VariantSku = ledger.Variant.Sku,
                ProductName = ledger.Variant.Product.Name,
                VariantCostAdjustment = ledger.Variant.CostAdjustment,
                ProductCost = ledger.Variant.Product.CostPrice,
                CreatedByName = ledger.Creator != null ? ledger.Creator.Name : null
            })
            .ToListAsync(cancellationToken);

        if (ledgerRows.Count == 0)
        {
            return new ProductLedgerReportReadModel(
                filter.ProductId,
                product.Name,
                product.Sku,
                filter.VariantId,
                variantName,
                Array.Empty<ProductLedgerRowReadModel>(),
                0,
                new ProductLedgerSummaryReadModel(opening, 0, 0, opening, 0m));
        }

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

        var rows = new List<ProductLedgerRowReadModel>(ledgerRows.Count);
        var totalIn = 0;
        var totalOut = 0;
        var totalValue = 0m;
        foreach (var row in ledgerRows)
        {
            var unitCost = row.ProductCost + row.VariantCostAdjustment;
            var quantity = row.QtyIn + row.QtyOut;
            totalIn += row.QtyIn;
            totalOut += row.QtyOut;
            totalValue += unitCost * quantity;

            var locationName = row.LocationType == "outlet"
                ? outletNames.GetValueOrDefault(row.LocationId, $"Outlet {row.LocationId}")
                : warehouseNames.GetValueOrDefault(row.LocationId, $"Warehouse {row.LocationId}");
            var referenceNumber = row.ReferenceType switch
            {
                StockLedgerReferenceType.Sale => saleNumbers.TryGetValue(row.ReferenceId, out var saleNumber)
                    ? saleNumber
                    : $"SALE-{row.ReferenceId}",
                StockLedgerReferenceType.Grn => grnNumbers.TryGetValue(row.ReferenceId, out var grnNumber)
                    ? grnNumber
                    : $"GRN-{row.ReferenceId}",
                StockLedgerReferenceType.StockTransfer => $"TRF-{row.ReferenceId}",
                StockLedgerReferenceType.StockAdjustment => $"ADJ-{row.ReferenceId}",
                _ => $"{row.ReferenceType}-{row.ReferenceId}"
            };

            rows.Add(new ProductLedgerRowReadModel(
                row.Id,
                row.CreatedAt,
                row.TransactionType,
                GetTransactionTypeLabel(row.TransactionType),
                referenceNumber,
                row.VariantId,
                row.ProductName,
                row.VariantSku,
                row.LocationId,
                locationName,
                row.BalanceAfter - row.QtyIn + row.QtyOut,
                row.QtyIn,
                row.QtyOut,
                row.BalanceAfter,
                unitCost,
                unitCost * quantity,
                row.CreatedByName,
                row.Remarks));
        }

        var summary = new ProductLedgerSummaryReadModel(
            opening,
            totalIn,
            totalOut,
            opening + totalIn - totalOut,
            totalValue);
        var pageRows = filter.PageSize > 0
            ? rows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToArray()
            : rows.ToArray();

        return new ProductLedgerReportReadModel(
            filter.ProductId,
            product.Name,
            product.Sku,
            filter.VariantId,
            variantName,
            pageRows,
            rows.Count,
            summary);
    }

    private static IQueryable<StockLedger> ApplyLocationScope(
        IQueryable<StockLedger> query,
        ProductLedgerReportFilter filter,
        IReadOnlyCollection<long> outletIds,
        IReadOnlyCollection<long> warehouseIds)
    {
        if (filter.OutletId.HasValue)
        {
            if (!outletIds.Contains(filter.OutletId.Value))
            {
                return query.Where(_ => false);
            }

            return query.Where(ledger =>
                ledger.LocationType == "outlet" && ledger.LocationId == filter.OutletId.Value);
        }
        if (filter.WarehouseId.HasValue)
        {
            if (!warehouseIds.Contains(filter.WarehouseId.Value))
            {
                return query.Where(_ => false);
            }

            return query.Where(ledger =>
                ledger.LocationType == "warehouse" && ledger.LocationId == filter.WarehouseId.Value);
        }

        var allowedOutlets = outletIds.ToList();
        var allowedWarehouses = warehouseIds.ToList();
        return query.Where(ledger =>
            ledger.LocationType == "outlet" && allowedOutlets.Contains(ledger.LocationId)
            || ledger.LocationType == "warehouse" && allowedWarehouses.Contains(ledger.LocationId));
    }

    private static string GetTransactionTypeLabel(string type) => type switch
    {
        StockLedgerTransactionType.Grn => "Purchase (GRN)",
        StockLedgerTransactionType.Sale => "Sale",
        StockLedgerTransactionType.Return => "Sales Return",
        StockLedgerTransactionType.Adjustment => "Stock Adjustment",
        StockLedgerTransactionType.TransferIn => "Transfer In",
        StockLedgerTransactionType.TransferOut => "Transfer Out",
        StockLedgerTransactionType.Exchange => "Exchange",
        _ => type
    };
}
