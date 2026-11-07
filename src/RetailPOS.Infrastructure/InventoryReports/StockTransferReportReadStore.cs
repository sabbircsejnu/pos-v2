using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Queries;

namespace RetailPOS.Infrastructure.InventoryReports;

public sealed class StockTransferReportReadStore(RetailPOSDbContext dbContext)
    : IStockTransferReportReadStore
{
    public async Task<StockTransferReportReadModel> GetAsync(
        StockTransferReadScope scope,
        StockTransferReportFilter filter,
        CancellationToken cancellationToken)
    {
        var outletIds = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.BusinessId == scope.BusinessId)
            .Select(outlet => outlet.Id)
            .ToListAsync(cancellationToken);
        var warehouseIds = await dbContext.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.BusinessId == scope.BusinessId)
            .Select(warehouse => warehouse.Id)
            .ToListAsync(cancellationToken);

        var query = dbContext.StockTransfers
            .AsNoTracking()
            .Include(transfer => transfer.Items)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
                        .ThenInclude(product => product.Category)
            .Include(transfer => transfer.Creator)
            .Where(transfer =>
                ((transfer.FromLocationType.ToLower() == "outlet"
                    && outletIds.Contains(transfer.FromLocationId))
                 || (transfer.FromLocationType.ToLower() == "warehouse"
                    && warehouseIds.Contains(transfer.FromLocationId)))
                && ((transfer.ToLocationType.ToLower() == "outlet"
                    && outletIds.Contains(transfer.ToLocationId))
                 || (transfer.ToLocationType.ToLower() == "warehouse"
                    && warehouseIds.Contains(transfer.ToLocationId))))
            .ApplyVisibility(
                scope.CanViewAllLocations,
                scope.DefaultLocationType,
                scope.DefaultLocationId);

        if (filter.DateFrom.HasValue)
        {
            query = query.Where(transfer => transfer.TransferDate >= filter.DateFrom.Value);
        }

        if (filter.DateTo.HasValue)
        {
            var dateToExclusive = filter.DateTo.Value.AddDays(1);
            query = query.Where(transfer => transfer.TransferDate < dateToExclusive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.ToLower();
            query = query.Where(transfer => transfer.Status == status);
        }

        if (filter.FromOutletId.HasValue)
        {
            query = query.Where(transfer =>
                transfer.FromLocationType == "outlet"
                && transfer.FromLocationId == filter.FromOutletId.Value);
        }

        if (filter.ToOutletId.HasValue)
        {
            query = query.Where(transfer =>
                transfer.ToLocationType == "outlet"
                && transfer.ToLocationId == filter.ToOutletId.Value);
        }

        var transfers = await query.ToListAsync(cancellationToken);
        if (transfers.Count == 0 || transfers.All(transfer => !transfer.Items.Any()))
        {
            return EmptyReport();
        }

        var fromOutletIds = transfers
            .Where(transfer => transfer.FromLocationType == "outlet")
            .Select(transfer => transfer.FromLocationId);
        var toOutletIds = transfers
            .Where(transfer => transfer.ToLocationType == "outlet")
            .Select(transfer => transfer.ToLocationId);
        var outletLocationIds = fromOutletIds.Union(toOutletIds).Distinct().ToList();
        var fromWarehouseIds = transfers
            .Where(transfer => transfer.FromLocationType == "warehouse")
            .Select(transfer => transfer.FromLocationId);
        var toWarehouseIds = transfers
            .Where(transfer => transfer.ToLocationType == "warehouse")
            .Select(transfer => transfer.ToLocationId);
        var warehouseLocationIds = fromWarehouseIds.Union(toWarehouseIds).Distinct().ToList();

        var outletNames = outletLocationIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Outlets
                .AsNoTracking()
                .Where(outlet =>
                    outlet.BusinessId == scope.BusinessId
                    && outletLocationIds.Contains(outlet.Id))
                .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);
        var warehouseNames = warehouseLocationIds.Count == 0
            ? new Dictionary<long, string>()
            : await dbContext.Warehouses
                .AsNoTracking()
                .Where(warehouse =>
                    warehouse.BusinessId == scope.BusinessId
                    && warehouseLocationIds.Contains(warehouse.Id))
                .ToDictionaryAsync(
                    warehouse => warehouse.Id,
                    warehouse => warehouse.Name,
                    cancellationToken);

        string ResolveLocationName(long locationId, string locationType) =>
            locationType == "outlet"
                ? outletNames.GetValueOrDefault(locationId, $"Outlet {locationId}")
                : warehouseNames.GetValueOrDefault(locationId, $"Warehouse {locationId}");

        var search = filter.Search.Trim().ToLower();
        var rows = new List<StockTransferReportRowReadModel>();
        var rowVariantIds = new List<long>();
        foreach (var transfer in transfers)
        {
            var creatorName = transfer.Creator?.Name ?? string.Empty;
            var fromLocationName = ResolveLocationName(
                transfer.FromLocationId,
                transfer.FromLocationType);
            var toLocationName = ResolveLocationName(
                transfer.ToLocationId,
                transfer.ToLocationType);

            foreach (var item in transfer.Items)
            {
                if (filter.CategoryId.HasValue
                    && item.Variant.Product.CategoryId != filter.CategoryId.Value)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(search)
                    && !item.Variant.Sku.ToLower().Contains(search)
                    && !item.Variant.Product.Name.ToLower().Contains(search)
                    && !(item.Variant.Product.ProductCode?.ToLower().Contains(search) ?? false))
                {
                    continue;
                }

                var unitCost = item.Variant.Product.CostPrice + item.Variant.CostAdjustment;
                rowVariantIds.Add(item.VariantId);
                rows.Add(new StockTransferReportRowReadModel(
                    transfer.Id,
                    transfer.TransferDate,
                    fromLocationName,
                    transfer.FromLocationType,
                    toLocationName,
                    transfer.ToLocationType,
                    transfer.Status,
                    item.Variant.Product.ProductCode ?? string.Empty,
                    item.Variant.Barcode ?? item.Variant.Product.Barcode ?? string.Empty,
                    item.Variant.Sku,
                    item.Variant.Product.Name,
                    item.Variant.Product.Category?.Name ?? "Uncategorized",
                    item.Quantity,
                    unitCost,
                    unitCost * item.Quantity,
                    creatorName));
            }
        }

        var nonPcsVariants = await RetailPOS.Infrastructure.Uom.UomReportTotals.NonPcsVariantIdsAsync(dbContext, cancellationToken);
        var visibleTransferIds = rows.Select(row => row.TransferId).Distinct().ToList();
        var summary = new StockTransferReportSummaryReadModel(
            visibleTransferIds.Count,
            rows.Count,
            rows.Where((_, index) => !nonPcsVariants.Contains(rowVariantIds[index])).Sum(row => row.Quantity),
            rows.Sum(row => row.TransferValue),
            transfers.Count(transfer =>
                visibleTransferIds.Contains(transfer.Id) && transfer.Status == "pending"),
            transfers.Count(transfer =>
                visibleTransferIds.Contains(transfer.Id) && transfer.Status == "completed"));

        rows = (filter.SortBy.ToLower(), filter.SortDir.ToLower() == "desc") switch
        {
            ("transferdate", true) => rows.OrderByDescending(row => row.TransferDate).ToList(),
            ("transferdate", false) => rows.OrderBy(row => row.TransferDate).ToList(),
            ("productname", true) => rows.OrderByDescending(row => row.ProductName).ToList(),
            ("productname", false) => rows.OrderBy(row => row.ProductName).ToList(),
            ("fromlocation", true) => rows.OrderByDescending(row => row.FromLocationName).ToList(),
            ("fromlocation", false) => rows.OrderBy(row => row.FromLocationName).ToList(),
            ("tolocation", true) => rows.OrderByDescending(row => row.ToLocationName).ToList(),
            ("tolocation", false) => rows.OrderBy(row => row.ToLocationName).ToList(),
            ("quantity", true) => rows.OrderByDescending(row => row.Quantity).ToList(),
            ("quantity", false) => rows.OrderBy(row => row.Quantity).ToList(),
            ("transfervalue", true) => rows.OrderByDescending(row => row.TransferValue).ToList(),
            ("transfervalue", false) => rows.OrderBy(row => row.TransferValue).ToList(),
            ("status", true) => rows.OrderByDescending(row => row.Status).ToList(),
            ("status", false) => rows.OrderBy(row => row.Status).ToList(),
            _ => rows.OrderByDescending(row => row.TransferDate).ToList()
        };

        var totalCount = rows.Count;
        if (filter.PageSize > 0)
        {
            rows = rows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        }

        return new StockTransferReportReadModel(rows, totalCount, summary);
    }

    private static StockTransferReportReadModel EmptyReport() =>
        new(
            Array.Empty<StockTransferReportRowReadModel>(),
            0,
            new StockTransferReportSummaryReadModel(0, 0, 0, 0m, 0, 0));
}
