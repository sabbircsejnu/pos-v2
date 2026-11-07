using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Application.OwnerDashboards;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;

namespace RetailPOS.Infrastructure.OwnerDashboards;

public sealed class InventoryDashboardReadStore(RetailPOSDbContext dbContext, TimeProvider clock)
    : IInventoryDashboardReadStore
{
    private sealed class LedgerState(DateOnly from, DateOnly to)
    {
        public InStockDayTracker Days { get; } = new(from, to);
        public (long Sequence, long Id) Latest { get; set; } = (long.MinValue, long.MinValue);
        public decimal LatestBalance { get; set; }
    }

    private sealed record SalesTotal(decimal Units, decimal Net);

    public async Task<IReadOnlyList<CategoryOption>> GetCategoriesAsync(
        long businessId,
        CancellationToken cancellationToken) =>
        await dbContext.Categories
            .AsNoTracking()
            .Where(category => category.BusinessId == businessId && category.IsActive)
            .OrderBy(category => category.Name)
            .Select(category => new CategoryOption(category.Id, category.Name))
            .ToListAsync(cancellationToken);

    public async Task<BusinessLocations> GetBusinessLocationsAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        var outlets = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.BusinessId == businessId)
            .OrderBy(outlet => outlet.Name)
            .Select(outlet => new OwnerDashboardLocation(outlet.Id, outlet.Name, OwnerDashboardLocationTypes.Outlet))
            .ToListAsync(cancellationToken);
        var warehouses = await dbContext.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.BusinessId == businessId)
            .OrderBy(warehouse => warehouse.Name)
            .Select(warehouse => new OwnerDashboardLocation(warehouse.Id, warehouse.Name, OwnerDashboardLocationTypes.Warehouse))
            .ToListAsync(cancellationToken);
        return new BusinessLocations(outlets, warehouses);
    }

    public Task<InventorySnapshot> GetSnapshotAsync(
        OwnerDashboardScope scope,
        DateOnly from,
        DateOnly to,
        bool includeCost,
        CancellationToken cancellationToken) =>
        OwnerDashboardSales.InReadOnlySnapshotAsync(
            dbContext,
            () => ReadSnapshotAsync(scope, from, to, includeCost, cancellationToken),
            cancellationToken);

    private async Task<InventorySnapshot> ReadSnapshotAsync(
        OwnerDashboardScope scope,
        DateOnly from,
        DateOnly to,
        bool includeCost,
        CancellationToken cancellationToken)
    {
        var asOfUtc = clock.GetUtcNow().UtcDateTime;
        var outletIds = scope.OutletIds;
        var warehouseIds = scope.WarehouseIds;
        var outletNames = scope.Outlets.ToDictionary(outlet => outlet.Id, outlet => outlet.Name);
        var warehouseNames = scope.Warehouses.ToDictionary(warehouse => warehouse.Id, warehouse => warehouse.Name);

        var stock = await dbContext.Inventories
            .AsNoTracking()
            .Where(inventory =>
                inventory.Variant.BusinessId == scope.BusinessId
                && ((inventory.LocationType == OwnerDashboardLocationTypes.Outlet && outletIds.Contains(inventory.LocationId))
                    || (inventory.LocationType == OwnerDashboardLocationTypes.Warehouse && warehouseIds.Contains(inventory.LocationId))))
            .GroupBy(inventory => new
            {
                inventory.VariantId,
                inventory.LocationId,
                inventory.LocationType,
                inventory.Variant.ProductId,
                MainCode = inventory.Variant.Product.ProductCode,
                ProductName = inventory.Variant.Product.Name,
                inventory.Variant.Sku,
                VariantName = inventory.Variant.Name,
                inventory.Variant.Product.CategoryId,
                CategoryName = inventory.Variant.Product.Category.Name,
                inventory.Variant.Product.Status
            })
            .Select(group => new { group.Key, OnHand = group.Sum(inventory => inventory.Quantity) })
            .ToListAsync(cancellationToken);

        var baseUnits = await RetailPOS.Infrastructure.Uom.UomReportTotals.BaseUnitCodesAsync(dbContext, scope.BusinessId, cancellationToken);
        // Packaging value inside prepared packs is inventory too; it just no longer sits in the packaging variant's pool.
        var capitalized = includeCost
            ? await dbContext.PackStateBalances.AsNoTracking()
                .Where(row => row.BusinessId == scope.BusinessId && row.CapitalizedValue != 0m)
                .GroupBy(row => new { row.VariantId, row.LocationId, row.LocationType })
                .Select(group => new { group.Key, Value = group.Sum(row => row.CapitalizedValue) })
                .ToDictionaryAsync(row => (row.Key.VariantId, row.Key.LocationId, row.Key.LocationType), row => row.Value, cancellationToken)
            : [];
        var sales = await ReadNetSalesAsync(scope, outletIds, from, to, cancellationToken);
        var lastSales = await ReadLastSalesAsync(scope, outletIds, cancellationToken);
        var (outbound, inbound) = await ReadPendingTransfersAsync(outletIds, warehouseIds, cancellationToken);
        var costLedger = new StockCostLedger();
        var ledger = await ReplayLedgerAsync(scope, outletIds, warehouseIds, from, to, includeCost, costLedger, cancellationToken);
        var costEvidence = includeCost
            ? await StockCostReader.ReadEvidenceAsync(dbContext, costLedger, cancellationToken)
            : StockCostEvidence.None;

        var facts = stock.Select(row =>
        {
            var key = row.Key;
            var locationKey = (key.VariantId, key.LocationId, key.LocationType);
            ledger.TryGetValue(locationKey, out var state);
            var isOutlet = key.LocationType == OwnerDashboardLocationTypes.Outlet;
            var sold = isOutlet ? sales.GetValueOrDefault((key.LocationId, key.VariantId)) : null;
            var lastSale = isOutlet && lastSales.TryGetValue((key.LocationId, key.VariantId), out var date) ? date : (DateOnly?)null;
            var (unitCost, value) = includeCost
                ? Value(row.OnHand, costLedger.Classify(
                    key.VariantId,
                    new StockCostLocation(key.LocationId, key.LocationType),
                    row.OnHand,
                    costEvidence))
                : (null, null);

            return new VariantLocationFact(
                key.VariantId,
                key.ProductId,
                string.IsNullOrWhiteSpace(key.MainCode) ? null : key.MainCode,
                key.ProductName,
                key.Sku,
                string.IsNullOrWhiteSpace(key.VariantName) ? key.Sku : key.VariantName,
                key.CategoryId,
                key.CategoryName,
                key.Status == ProductStatus.Active,
                key.LocationId,
                key.LocationType,
                isOutlet
                    ? outletNames.GetValueOrDefault(key.LocationId, string.Empty)
                    : warehouseNames.GetValueOrDefault(key.LocationId, string.Empty),
                row.OnHand,
                outbound.GetValueOrDefault(locationKey),
                inbound.GetValueOrDefault(locationKey),
                sold?.Units ?? 0m,
                sold?.Net ?? 0m,
                state?.Days.Complete() ?? 0,
                // History that does not end at the current balance cannot support a demand claim.
                state is not null && state.Days.HasEvidence && state.LatestBalance == row.OnHand,
                lastSale is not null,
                lastSale,
                unitCost,
                value is null ? null : value + capitalized.GetValueOrDefault(locationKey),
                baseUnits.GetValueOrDefault(key.VariantId, "pcs"));
        }).ToList();

        return new InventorySnapshot(facts, asOfUtc);
    }

    /// <summary>A null value means the cost is unknown; a zero on stock means documents prove it was free. Master cost is never substituted.</summary>
    private static (decimal? UnitCost, decimal? Value) Value(decimal onHand, StockCostPosition position) =>
        onHand <= 0 ? (null, 0m) : (position.UnitCost, position.Value);

    private async Task<Dictionary<(long Outlet, long Variant), SalesTotal>> ReadNetSalesAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<(long, long), SalesTotal>();
        if (outletIds.Count == 0)
            return result;

        var rows = await OwnerDashboardSales.InPeriod(dbContext, scope.BusinessId, outletIds, from, to, scope.TimeZoneId)
            .SelectMany(sale => sale.Items.Select(item => new
            {
                sale.OutletId,
                item.VariantId,
                item.Quantity,
                Net = item.TaxableAmount ?? item.Subtotal
            }))
            .GroupBy(line => new { line.OutletId, line.VariantId })
            .Select(group => new
            {
                group.Key.OutletId,
                group.Key.VariantId,
                Units = group.Sum(line => line.Quantity),
                Net = group.Sum(line => line.Net)
            })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
            result[(row.OutletId, row.VariantId)] = new SalesTotal(row.Units, row.Net);

        var reversals = await OwnerDashboardSales.ReversalsAsync(
            dbContext,
            scope.BusinessId,
            outletIds,
            from,
            to,
            scope.TimeZoneId,
            cancellationToken);
        foreach (var reversal in reversals.Where(reversal => reversal.Trusted))
        {
            foreach (var line in reversal.Snapshot.Lines)
            {
                var key = (reversal.OutletId, line.VariantId);
                var total = result.GetValueOrDefault(key) ?? new SalesTotal(0m, 0m);
                result[key] = new SalesTotal(total.Units - line.Quantity, total.Net - (line.NetRevenue ?? 0m));
            }
        }

        return result;
    }

    private async Task<Dictionary<(long Outlet, long Variant), DateOnly>> ReadLastSalesAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        CancellationToken cancellationToken)
    {
        if (outletIds.Count == 0)
            return [];

        var zone = scope.TimeZoneId;
        var rows = await dbContext.SaleItems
            .AsNoTracking()
            .Where(item =>
                outletIds.Contains(item.Sale.OutletId)
                && (item.Sale.BusinessId == null || item.Sale.BusinessId == scope.BusinessId)
                && (item.Sale.Status.ToLower() == "completed" || item.Sale.Status.ToLower() == "refunded"))
            .GroupBy(item => new { item.Sale.OutletId, item.VariantId })
            .Select(group => new
            {
                group.Key.OutletId,
                group.Key.VariantId,
                Last = group.Max(item => item.Sale.BusinessDate ?? TimeZoneInfo.ConvertTimeBySystemTimeZoneId(item.Sale.SaleDate, zone).Date)
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => (row.OutletId, row.VariantId), row => DateOnly.FromDateTime(row.Last));
    }

    /// <summary>
    /// Stock leaves the source at dispatch and reaches the destination at receipt. So drafts, submitted and approved
    /// transfers are still source commitments, while in-transit units only count as inbound.
    /// </summary>
    private async Task<(Dictionary<(long, long, string), decimal> Outbound, Dictionary<(long, long, string), decimal> Inbound)>
        ReadPendingTransfersAsync(
            List<long> outletIds,
            List<long> warehouseIds,
            CancellationToken cancellationToken)
    {
        var outboundRows = await dbContext.StockTransferItems
            .AsNoTracking()
            .Where(item =>
                (item.Transfer.Status == StockTransfer.StatusDraft
                    || item.Transfer.Status == StockTransfer.StatusSubmitted
                    || item.Transfer.Status == StockTransfer.StatusApproved)
                && ((item.Transfer.FromLocationType == OwnerDashboardLocationTypes.Outlet && outletIds.Contains(item.Transfer.FromLocationId))
                    || (item.Transfer.FromLocationType == OwnerDashboardLocationTypes.Warehouse && warehouseIds.Contains(item.Transfer.FromLocationId))))
            .GroupBy(item => new { item.VariantId, item.Transfer.FromLocationId, item.Transfer.FromLocationType })
            .Select(group => new
            {
                group.Key.VariantId,
                group.Key.FromLocationId,
                group.Key.FromLocationType,
                Quantity = group.Sum(item => item.TransferQuantity > 0 ? item.TransferQuantity : item.Quantity)
            })
            .ToListAsync(cancellationToken);

        var inboundRows = await dbContext.StockTransferItems
            .AsNoTracking()
            .Where(item =>
                (item.Transfer.Status == StockTransfer.StatusDraft
                    || item.Transfer.Status == StockTransfer.StatusSubmitted
                    || item.Transfer.Status == StockTransfer.StatusApproved
                    || item.Transfer.Status == StockTransfer.StatusInTransit)
                && ((item.Transfer.ToLocationType == OwnerDashboardLocationTypes.Outlet && outletIds.Contains(item.Transfer.ToLocationId))
                    || (item.Transfer.ToLocationType == OwnerDashboardLocationTypes.Warehouse && warehouseIds.Contains(item.Transfer.ToLocationId))))
            .GroupBy(item => new { item.VariantId, item.Transfer.ToLocationId, item.Transfer.ToLocationType })
            .Select(group => new
            {
                group.Key.VariantId,
                group.Key.ToLocationId,
                group.Key.ToLocationType,
                Quantity = group.Sum(item => item.TransferQuantity > 0 ? item.TransferQuantity : item.Quantity)
            })
            .ToListAsync(cancellationToken);

        return (
            outboundRows.ToDictionary(row => (row.VariantId, row.FromLocationId, row.FromLocationType), row => row.Quantity),
            inboundRows.ToDictionary(row => (row.VariantId, row.ToLocationId, row.ToLocationType), row => row.Quantity));
    }

    /// <summary>Streams the ledger once, holding one small state per variant and location rather than the history.</summary>
    private async Task<Dictionary<(long, long, string), LedgerState>> ReplayLedgerAsync(
        OwnerDashboardScope scope,
        List<long> outletIds,
        List<long> warehouseIds,
        DateOnly from,
        DateOnly to,
        bool includeCost,
        StockCostLedger costLedger,
        CancellationToken cancellationToken)
    {
        var states = new Dictionary<(long, long, string), LedgerState>();
        var corrections = includeCost
            ? await StockCostReader.ReadCorrectionsAsync(dbContext, scope.BusinessId, null, cancellationToken)
            : StockCostCorrectionSet.Empty;
        var movements = dbContext.StockLedgers
            .AsNoTracking()
            .Where(movement =>
                movement.Variant.BusinessId == scope.BusinessId
                && ((movement.LocationType == OwnerDashboardLocationTypes.Outlet && outletIds.Contains(movement.LocationId))
                    || (movement.LocationType == OwnerDashboardLocationTypes.Warehouse && warehouseIds.Contains(movement.LocationId))))
            .OrderBy(movement => movement.BusinessDate)
            .ThenBy(movement => movement.MovementSequence)
            .ThenBy(movement => movement.Id)
            .Select(movement => new
            {
                movement.Id,
                movement.VariantId,
                movement.LocationId,
                movement.LocationType,
                movement.QtyIn,
                movement.QtyOut,
                movement.BalanceAfter,
                movement.UnitCost,
                movement.MovementValue,
                movement.IsCostValuationReliable,
                movement.BusinessDate,
                movement.MovementSequence,
                movement.ReferenceType,
                movement.ReferenceId
            })
            .AsAsyncEnumerable();

        await foreach (var movement in movements.WithCancellation(cancellationToken))
        {
            var key = (movement.VariantId, movement.LocationId, movement.LocationType);
            if (!states.TryGetValue(key, out var state))
            {
                state = new LedgerState(from, to);
                states[key] = state;
            }

            state.Days.Apply(DateOnly.FromDateTime(movement.BusinessDate), movement.BalanceAfter);

            // A late sale has an earlier business date but a later sequence: the current balance follows the sequence.
            if ((movement.MovementSequence, movement.Id).CompareTo(state.Latest) > 0)
            {
                state.Latest = (movement.MovementSequence, movement.Id);
                state.LatestBalance = movement.BalanceAfter;
            }

            if (includeCost)
            {
                var (unitCost, value, isReliable) = corrections.Resolve(
                    movement.Id,
                    movement.UnitCost,
                    movement.MovementValue,
                    movement.IsCostValuationReliable);
                costLedger.Apply(new StockCostMovement(
                    movement.VariantId,
                    movement.LocationId,
                    movement.LocationType,
                    movement.ReferenceType,
                    movement.ReferenceId,
                    movement.QtyIn,
                    movement.QtyOut,
                    unitCost,
                    value,
                    isReliable));
            }
        }

        return states;
    }
}
