using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public sealed class MobileStockTakeReadStore(RetailPOSDbContext dbContext)
    : IMobileStockTakeReadStore
{
    public async Task<MobileStockTakePageReadModel> SearchAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? outletId,
        string? status,
        string? search,
        int page,
        int pageSize,
        long? ownerUserId,
        CancellationToken cancellationToken)
    {
        var query = AuthorizedSessions(businessId, authorizedOutletIds, ownerUserId);
        if (outletId.HasValue)
            query = query.Where(stockCount => stockCount.LocationId == outletId.Value);
        if (status is not null)
            query = query.Where(stockCount => stockCount.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(stockCount =>
                stockCount.StockCountNo.Contains(term)
                || (stockCount.Remarks != null && stockCount.Remarks.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(stockCount => stockCount.CreatedAt)
            .ThenByDescending(stockCount => stockCount.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(stockCount => new MobileStockTakeSummaryReadModel(
                stockCount.Id,
                stockCount.StockCountNo,
                stockCount.LocationId,
                dbContext.Outlets
                    .Where(outlet => outlet.Id == stockCount.LocationId
                                     && outlet.BusinessId == businessId)
                    .Select(outlet => outlet.Name)
                    .First(),
                stockCount.StockCountDate,
                stockCount.Status,
                stockCount.Remarks,
                stockCount.TotalItems,
                stockCount.CreatedAt,
                stockCount.SubmittedAt,
                stockCount.CountRevision,
                stockCount.BaselineConsistencyStatus,
                stockCount.BaselineInventoryReadStartedAtUtc,
                stockCount.BaselineInventoryReadCompletedAtUtc,
                stockCount.MovementReconciliationStatus))
            .ToArrayAsync(cancellationToken);

        return new MobileStockTakePageReadModel(items, page, pageSize, totalCount);
    }

    public Task<MobileStockTakeSummaryReadModel?> GetByIdAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId,
        long? ownerUserId,
        CancellationToken cancellationToken) =>
        AuthorizedSessions(businessId, authorizedOutletIds, ownerUserId)
            .Where(stockCount => stockCount.Id == stockCountId)
            .Select(stockCount => new MobileStockTakeSummaryReadModel(
                stockCount.Id,
                stockCount.StockCountNo,
                stockCount.LocationId,
                dbContext.Outlets
                    .Where(outlet => outlet.Id == stockCount.LocationId
                                     && outlet.BusinessId == businessId)
                    .Select(outlet => outlet.Name)
                    .First(),
                stockCount.StockCountDate,
                stockCount.Status,
                stockCount.Remarks,
                stockCount.TotalItems,
                stockCount.CreatedAt,
                stockCount.SubmittedAt,
                stockCount.CountRevision,
                stockCount.BaselineConsistencyStatus,
                stockCount.BaselineInventoryReadStartedAtUtc,
                stockCount.BaselineInventoryReadCompletedAtUtc,
                stockCount.MovementReconciliationStatus))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<MobileStockTakeLinePageReadModel?> GetLinesAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId,
        int page,
        int pageSize,
        long? ownerUserId,
        CancellationToken cancellationToken)
    {
        var authorizedSession = await AuthorizedSessions(businessId, authorizedOutletIds, ownerUserId)
            .AnyAsync(stockCount => stockCount.Id == stockCountId, cancellationToken);
        if (!authorizedSession)
            return null;

        var query = dbContext.StockCountLines
            .AsNoTracking()
            .Where(line => line.StockCountId == stockCountId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(line => line.ProductName)
            .ThenBy(line => line.VariantName)
            .ThenBy(line => line.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(line => new MobileStockTakeLineReadModel(
                line.Id,
                line.ProductId,
                line.VariantId,
                line.ProductName,
                line.ProductCode,
                line.VariantName,
                line.PhysicalStock,
                line.Remarks))
            .ToArrayAsync(cancellationToken);

        return new MobileStockTakeLinePageReadModel(items, page, pageSize, totalCount);
    }

    private IQueryable<StockCount> AuthorizedSessions(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId) =>
        dbContext.StockCounts
            .AsNoTracking()
            .Where(stockCount =>
                stockCount.BusinessId == businessId
                && stockCount.LocationType == "outlet"
                && authorizedOutletIds.Contains(stockCount.LocationId)
                && dbContext.Outlets.Any(outlet =>
                    outlet.Id == stockCount.LocationId
                    && outlet.BusinessId == businessId)
                && (ownerUserId == null
                    || stockCount.CreatedBy == ownerUserId
                    || stockCount.SubmittedBy == ownerUserId));
}
