using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public sealed class StockCountSearchReadStore(
    RetailPOSDbContext dbContext,
    IStockCountAsyncAdjustmentSwitch asyncAdjustment)
    : IStockCountSearchReadStore
{
    public async Task<StockCountDetailReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var stockCount = await dbContext.StockCounts
            .AsNoTracking()
            .Include(item => item.Creator)
            .Include(item => item.Lines)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (stockCount is null)
        {
            return null;
        }

        var businessName = await dbContext.Businesses
            .AsNoTracking()
            .Where(business => business.Id == stockCount.BusinessId)
            .Select(business => business.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Business";
        var locationName = await GetLocationNameAsync(
            stockCount.LocationId,
            stockCount.LocationType,
            cancellationToken);
        var movement = await GetMovementSummaryAsync(
            stockCount.Id,
            stockCount.LocationId,
            stockCount.LocationType,
            stockCount.CreatedAt,
            cancellationToken);

        var latestAdjustmentJob = asyncAdjustment.Enabled
            ? await dbContext.StockCountAdjustmentJobs
                .AsNoTracking()
                .Where(job => job.StockCountId == stockCount.Id)
                .OrderByDescending(job => job.CreatedAtUtc)
                .ThenByDescending(job => job.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new StockCountDetailReadModel(
            stockCount.Id,
            stockCount.StockCountNo,
            stockCount.BusinessId,
            businessName,
            stockCount.LocationId,
            stockCount.LocationType,
            locationName,
            stockCount.StockCountDate,
            stockCount.Status,
            stockCount.Remarks,
            stockCount.TotalItems,
            stockCount.CreatedBy,
            stockCount.Creator?.Name ?? string.Empty,
            stockCount.CreatedAt,
            stockCount.SubmittedBy,
            stockCount.SubmittedAt,
            stockCount.ApprovedBy,
            stockCount.ApprovedAt,
            stockCount.RejectedBy,
            stockCount.RejectedAt,
            stockCount.RejectionReason,
            movement.HasMovement,
            movement.Count,
            movement.LastMovementAt,
            stockCount.Lines
                .OrderBy(line => line.ProductName)
                .ThenBy(line => line.VariantName)
                .Select(line => new StockCountLineReadModel(
                    line.Id,
                    line.ProductId,
                    line.VariantId,
                    line.ProductName,
                    line.ProductCode,
                    line.VariantName,
                    line.CurrentStock,
                    line.PhysicalStock,
                    line.Difference,
                    line.Remarks))
                .ToArray(),
            latestAdjustmentJob is null ? null : StockCountAdjustmentJobStore.ToReadModel(latestAdjustmentJob));
    }

    public async Task<StockCountSearchResultReadModel> SearchAsync(
        StockCountReadScope scope,
        StockCountSearchFilter filter,
        CancellationToken cancellationToken)
    {
        var query = dbContext.StockCounts
            .AsNoTracking()
            .Include(stockCount => stockCount.Creator)
            .AsQueryable();

        if (scope.BusinessId.HasValue)
        {
            query = query.Where(stockCount => stockCount.BusinessId == scope.BusinessId.Value);
        }

        query = query.Where(stockCount =>
            (stockCount.LocationType == "outlet" && scope.AuthorizedOutletIds.Contains(stockCount.LocationId))
            || (stockCount.LocationType == "warehouse" && scope.AuthorizedWarehouseIds.Contains(stockCount.LocationId)));

        if (scope.LocationId.HasValue)
        {
            query = query.Where(stockCount =>
                stockCount.LocationId == scope.LocationId.Value
                && stockCount.LocationType == scope.LocationType);
        }
        else if (!string.IsNullOrWhiteSpace(scope.LocationType))
        {
            query = query.Where(stockCount => stockCount.LocationType == scope.LocationType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(stockCount => stockCount.Status == filter.Status);
        }

        if (filter.DateFrom.HasValue)
        {
            query = query.Where(stockCount => stockCount.StockCountDate >= filter.DateFrom.Value.Date);
        }

        if (filter.DateTo.HasValue)
        {
            var inclusiveTo = filter.DateTo.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(stockCount => stockCount.StockCountDate <= inclusiveTo);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(stockCount =>
                stockCount.StockCountNo.Contains(term)
                || (stockCount.Remarks != null && stockCount.Remarks.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = Math.Max(filter.PageNumber, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 500);
        var items = await query
            .OrderByDescending(stockCount => stockCount.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var results = new List<StockCountSearchItemReadModel>(items.Count);
        foreach (var item in items)
        {
            var movement = await GetMovementSummaryAsync(
                item.Id,
                item.LocationId,
                item.LocationType,
                item.CreatedAt,
                cancellationToken);
            var locationName = await GetLocationNameAsync(
                item.LocationId,
                item.LocationType,
                cancellationToken);
            results.Add(new StockCountSearchItemReadModel(
                item.Id,
                item.StockCountNo,
                item.BusinessId,
                item.LocationId,
                item.LocationType,
                locationName,
                item.StockCountDate,
                item.Status,
                item.Remarks,
                item.TotalItems,
                item.CreatedBy,
                item.Creator?.Name ?? string.Empty,
                item.CreatedAt,
                item.SubmittedBy,
                item.SubmittedAt,
                item.ApprovedBy,
                item.ApprovedAt,
                item.RejectedBy,
                item.RejectedAt,
                item.RejectionReason,
                movement.HasMovement,
                movement.Count,
                movement.LastMovementAt));
        }

        return new StockCountSearchResultReadModel(results, totalCount, pageNumber, pageSize);
    }

    private async Task<string> GetLocationNameAsync(
        long locationId,
        string locationType,
        CancellationToken cancellationToken)
    {
        if (locationType == "warehouse")
        {
            return await dbContext.Warehouses
                .AsNoTracking()
                .Where(warehouse => warehouse.Id == locationId)
                .Select(warehouse => warehouse.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? $"Warehouse #{locationId}";
        }

        return await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.Id == locationId)
            .Select(outlet => outlet.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? $"Outlet #{locationId}";
    }

    private async Task<(bool HasMovement, int Count, DateTime? LastMovementAt)> GetMovementSummaryAsync(
        long stockCountId,
        long locationId,
        string locationType,
        DateTime generatedAt,
        CancellationToken cancellationToken)
    {
        var variantIds = await dbContext.StockCountLines
            .AsNoTracking()
            .Where(line => line.StockCountId == stockCountId)
            .Select(line => line.VariantId)
            .ToListAsync(cancellationToken);
        if (variantIds.Count == 0)
        {
            return (false, 0, null);
        }

        var movementQuery = dbContext.StockLedgers
            .AsNoTracking()
            .Where(ledger =>
                ledger.LocationId == locationId
                && ledger.LocationType == locationType
                && ledger.CreatedAt > generatedAt
                && variantIds.Contains(ledger.VariantId)
                // A cost correction moves no stock, so it cannot make a count out of date.
                && ledger.TransactionType != StockLedgerTransactionType.CostCorrection);

        var count = await movementQuery.CountAsync(cancellationToken);
        if (count == 0)
        {
            return (false, 0, null);
        }

        var lastAt = await movementQuery.MaxAsync(ledger => (DateTime?)ledger.CreatedAt, cancellationToken);
        return (true, count, lastAt);
    }
}
