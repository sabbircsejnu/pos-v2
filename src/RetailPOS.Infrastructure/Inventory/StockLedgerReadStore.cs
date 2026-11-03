using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockLedger;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockLedgerReadStore(RetailPOSDbContext dbContext)
    : IStockLedgerReadStore
{
    public async Task<StockLedgerReadModel?> GetByIdAsync(
        long id,
        StockLedgerReadScope scope,
        CancellationToken cancellationToken)
    {
        var entry = await ForScope(scope)
            .Where(ledger => ledger.Id == id)
            .Include(ledger => ledger.Variant)
                .ThenInclude(variant => variant.Product)
            .Include(ledger => ledger.Creator)
            .SingleOrDefaultAsync(cancellationToken);

        if (entry is null)
        {
            return null;
        }

        return (await MapAsync([entry], scope.BusinessId, cancellationToken))[0];
    }

    public async Task<IReadOnlyList<StockLedgerReadModel>> GetByVariantAndLocationAsync(
        long variantId,
        long locationId,
        string locationType,
        StockLedgerReadScope scope,
        CancellationToken cancellationToken)
    {
        var normalizedLocationType = locationType.Trim().ToLowerInvariant();
        var entries = await ForScope(scope)
            .Where(ledger =>
                ledger.VariantId == variantId
                && ledger.LocationId == locationId
                && ledger.LocationType.ToLower() == normalizedLocationType)
            .Include(ledger => ledger.Variant)
                .ThenInclude(variant => variant.Product)
            .Include(ledger => ledger.Creator)
            .OrderBy(ledger => ledger.CreatedAt)
            .ToListAsync(cancellationToken);

        return await MapAsync(entries, scope.BusinessId, cancellationToken);
    }

    public async Task<IReadOnlyList<StockLedgerReadModel>> GetByReferenceAsync(
        string referenceType,
        long referenceId,
        StockLedgerReadScope scope,
        CancellationToken cancellationToken)
    {
        var normalizedReferenceType = referenceType.ToLowerInvariant();
        var entries = await ForScope(scope)
            .Where(ledger =>
                ledger.ReferenceType.ToLower() == normalizedReferenceType
                && ledger.ReferenceId == referenceId)
            .Include(ledger => ledger.Variant)
                .ThenInclude(variant => variant.Product)
            .Include(ledger => ledger.Creator)
            .OrderBy(ledger => ledger.CreatedAt)
            .ToListAsync(cancellationToken);

        return await MapAsync(entries, scope.BusinessId, cancellationToken);
    }

    public async Task<StockLedgerSearchReadModel> SearchAsync(
        StockLedgerSearchFilter filter,
        StockLedgerReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = ForScope(scope);

        if (filter.VariantId.HasValue)
        {
            query = query.Where(ledger => ledger.VariantId == filter.VariantId.Value);
        }

        if (filter.LocationId.HasValue)
        {
            query = query.Where(ledger => ledger.LocationId == filter.LocationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.LocationType))
        {
            var locationType = filter.LocationType.ToLower();
            query = query.Where(ledger => ledger.LocationType.ToLower() == locationType);
        }

        if (!string.IsNullOrWhiteSpace(filter.TransactionType))
        {
            var transactionType = filter.TransactionType.ToLower();
            query = query.Where(ledger => ledger.TransactionType.ToLower() == transactionType);
        }

        if (!string.IsNullOrWhiteSpace(filter.ReferenceType))
        {
            var referenceType = filter.ReferenceType.ToLower();
            query = query.Where(ledger => ledger.ReferenceType.ToLower() == referenceType);
        }

        if (filter.ReferenceId.HasValue)
        {
            query = query.Where(ledger => ledger.ReferenceId == filter.ReferenceId.Value);
        }

        if (filter.StartDate.HasValue)
        {
            query = query.Where(ledger => ledger.CreatedAt >= filter.StartDate.Value);
        }

        if (filter.EndDate.HasValue)
        {
            query = query.Where(ledger => ledger.CreatedAt <= filter.EndDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var entries = await query
            .Include(ledger => ledger.Variant)
                .ThenInclude(variant => variant.Product)
            .Include(ledger => ledger.Creator)
            .OrderByDescending(ledger => ledger.CreatedAt)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        var readModels = await MapAsync(entries, scope.BusinessId, cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)filter.PageSize);

        return new StockLedgerSearchReadModel(
            readModels,
            totalCount,
            filter.PageNumber,
            filter.PageSize,
            totalPages,
            filter.PageNumber > 1,
            filter.PageNumber < totalPages);
    }

    private IQueryable<StockLedger> ForScope(StockLedgerReadScope scope)
    {
        var outletIds = scope.Locations
            .Where(location => location.Type.Equals("outlet", StringComparison.OrdinalIgnoreCase))
            .Select(location => location.Id)
            .Distinct()
            .ToList();
        var warehouseIds = scope.Locations
            .Where(location => location.Type.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
            .Select(location => location.Id)
            .Distinct()
            .ToList();

        return dbContext.StockLedgers
            .AsNoTracking()
            .Where(ledger =>
                (ledger.LocationType.ToLower() == "outlet"
                    && outletIds.Contains(ledger.LocationId)
                    && dbContext.Outlets.Any(outlet =>
                        outlet.Id == ledger.LocationId
                        && outlet.BusinessId == scope.BusinessId))
                || (ledger.LocationType.ToLower() == "warehouse"
                    && warehouseIds.Contains(ledger.LocationId)
                    && dbContext.Warehouses.Any(warehouse =>
                        warehouse.Id == ledger.LocationId
                        && warehouse.BusinessId == scope.BusinessId)));
    }

    private async Task<IReadOnlyList<StockLedgerReadModel>> MapAsync(
        IReadOnlyList<StockLedger> entries,
        long businessId,
        CancellationToken cancellationToken)
    {
        var outletIds = entries
            .Where(entry => entry.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.LocationId)
            .Distinct()
            .ToList();
        var warehouseIds = entries
            .Where(entry => entry.LocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.LocationId)
            .Distinct()
            .ToList();
        var outletNames = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.BusinessId == businessId && outletIds.Contains(outlet.Id))
            .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);
        var warehouseNames = await dbContext.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.BusinessId == businessId && warehouseIds.Contains(warehouse.Id))
            .ToDictionaryAsync(warehouse => warehouse.Id, warehouse => warehouse.Name, cancellationToken);

        return entries.Select(entry =>
        {
            var locationName = entry.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
                ? outletNames.GetValueOrDefault(entry.LocationId, $"Outlet {entry.LocationId}")
                : entry.LocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase)
                    ? warehouseNames.GetValueOrDefault(entry.LocationId, $"Warehouse {entry.LocationId}")
                    : $"Location {entry.LocationId}";

            return new StockLedgerReadModel(
                entry.Id,
                entry.VariantId,
                entry.Variant?.Sku ?? string.Empty,
                entry.Variant?.Product?.Name ?? string.Empty,
                entry.LocationId,
                entry.LocationType,
                locationName,
                entry.TransactionType,
                entry.QtyIn,
                entry.QtyOut,
                entry.BalanceAfter,
                entry.UnitCost,
                entry.MovementValue,
                entry.IsCostValuationReliable,
                entry.BusinessDate,
                entry.MovementSequence,
                entry.ReversalOfMovementId,
                entry.ReferenceType,
                entry.ReferenceId,
                entry.Remarks,
                entry.CreatedBy,
                entry.Creator?.Name,
                entry.CreatedAt);
        }).ToArray();
    }
}
