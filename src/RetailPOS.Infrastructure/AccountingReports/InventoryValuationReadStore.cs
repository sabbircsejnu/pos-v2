using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.AccountingReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingReports;

public sealed class InventoryValuationReadStore(RetailPOSDbContext dbContext)
    : IInventoryValuationReadStore
{
    public async Task<InventoryValuationReadModel> GetAsync(
        InventoryValuationFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.AccountingConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.BusinessId == businessId,
                cancellationToken);
        if (configuration?.CogsAccountId is null
            || configuration.InventoryAccountId is null)
            throw new InvalidOperationException(
                "COGS and Inventory accounts must be configured before running valuation reports.");

        var asOf = (filter.DateTo ?? DateTime.UtcNow)
            .Date
            .AddDays(1)
            .AddTicks(-1);
        var outletIds = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.BusinessId == businessId)
            .Select(outlet => outlet.Id)
            .ToListAsync(cancellationToken);
        var warehouseIds = await dbContext.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.BusinessId == businessId)
            .Select(warehouse => warehouse.Id)
            .ToListAsync(cancellationToken);
        var ledgerQuery = dbContext.StockLedgers
            .AsNoTracking()
            .Include(movement => movement.Variant)
            .ThenInclude(variant => variant.Product)
            .Where(movement => movement.BusinessDate <= asOf.Date)
            .Where(movement =>
                (movement.LocationType == "outlet"
                    && outletIds.Contains(movement.LocationId))
                || (movement.LocationType == "warehouse"
                    && warehouseIds.Contains(movement.LocationId)));
        if (filter.OutletId.HasValue)
            ledgerQuery = ledgerQuery.Where(movement =>
                movement.LocationType == "outlet"
                && movement.LocationId == filter.OutletId.Value);
        if (filter.WarehouseId.HasValue)
            ledgerQuery = ledgerQuery.Where(movement =>
                movement.LocationType == "warehouse"
                && movement.LocationId == filter.WarehouseId.Value);
        if (filter.LocationId.HasValue)
            ledgerQuery = ledgerQuery.Where(movement =>
                movement.LocationId == filter.LocationId.Value);
        if (!string.IsNullOrWhiteSpace(filter.LocationType))
            ledgerQuery = ledgerQuery.Where(movement =>
                movement.LocationType == filter.LocationType.Trim().ToLower());
        if (filter.ProductId.HasValue)
            ledgerQuery = ledgerQuery.Where(movement =>
                movement.Variant.ProductId == filter.ProductId.Value);
        if (filter.VariantId.HasValue)
            ledgerQuery = ledgerQuery.Where(movement =>
                movement.VariantId == filter.VariantId.Value);

        var movements = await ledgerQuery
            .OrderBy(movement => movement.VariantId)
            .ThenBy(movement => movement.LocationType)
            .ThenBy(movement => movement.LocationId)
            .ThenBy(movement => movement.BusinessDate)
            .ThenBy(movement => movement.MovementSequence)
            .ThenBy(movement => movement.Id)
            .ToListAsync(cancellationToken);
        var unvalued = movements
            .Where(movement => !movement.IsCostValuationReliable)
            .Select(movement => new UnvaluedStockMovementReadModel(
                movement.Id,
                movement.VariantId,
                movement.LocationId,
                movement.LocationType,
                movement.BusinessDate,
                movement.ReferenceType,
                movement.ReferenceId,
                movement.Remarks))
            .ToList();
        var rows = movements
            .Where(movement => movement.IsCostValuationReliable)
            .GroupBy(movement => new
            {
                movement.VariantId,
                movement.LocationId,
                movement.LocationType
            })
            .Select(group => ReplayPool(group.ToList()))
            .Where(row => row.Quantity != 0)
            .ToList();
        var ledgerValue = rows.Sum(row => row.InventoryValue);
        var glJournals = await ApplyScopeAndPeriod(
                dbContext.JournalEntries
                    .AsNoTracking()
                    .Where(journal => journal.BusinessId == businessId
                        && journal.Status == JournalEntryStatuses.Posted),
                filter)
            .Include(journal => journal.Lines)
            .ToListAsync(cancellationToken);
        var glBalance = glJournals
            .SelectMany(journal => journal.Lines)
            .Where(line => line.AccountId == configuration.InventoryAccountId.Value)
            .Sum(line => line.DebitAmount - line.CreditAmount);

        return new InventoryValuationReadModel(
            rows,
            unvalued,
            rows.Sum(row => row.Quantity),
            ledgerValue,
            glBalance,
            ledgerValue - glBalance,
            configuration.InventoryAccountId.Value,
            asOf.Date);
    }

    private static InventoryValuationRowReadModel ReplayPool(
        List<StockLedger> movements)
    {
        var first = movements[0];
        var quantity = 0;
        decimal value = 0;
        foreach (var movement in movements)
        {
            if (movement.QtyIn > 0)
            {
                quantity += movement.QtyIn;
                value += movement.MovementValue;
            }

            if (movement.QtyOut > 0)
            {
                var unitCost = quantity == 0
                    ? movement.UnitCost
                    : value / quantity;
                quantity -= movement.QtyOut;
                value -= Math.Round(
                    unitCost * movement.QtyOut,
                    2,
                    MidpointRounding.AwayFromZero);
            }
        }

        var product = first.Variant.Product;
        return new InventoryValuationRowReadModel(
            first.VariantId,
            product.Id,
            product.Name,
            product.ProductCode,
            first.Variant.Sku,
            first.LocationId,
            first.LocationType,
            quantity,
            quantity == 0 ? 0 : Math.Round(value / quantity, 4),
            value);
    }

    private static IQueryable<JournalEntry> ApplyScopeAndPeriod(
        IQueryable<JournalEntry> query,
        InventoryValuationFilter filter)
    {
        var end = (filter.DateTo ?? DateTime.UtcNow)
            .Date
            .AddDays(1)
            .AddTicks(-1);
        query = query.Where(journal => journal.PostingDate <= end);
        if (filter.OutletId.HasValue)
            query = query.Where(journal =>
                journal.OutletId == filter.OutletId.Value);
        if (filter.WarehouseId.HasValue)
            query = query.Where(journal =>
                journal.WarehouseId == filter.WarehouseId.Value);
        return query;
    }
}
