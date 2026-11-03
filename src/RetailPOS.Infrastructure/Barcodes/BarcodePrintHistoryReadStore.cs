using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Barcodes;

public sealed class BarcodePrintHistoryReadStore(RetailPOSDbContext dbContext)
    : IBarcodePrintHistoryReadStore
{
    public async Task<BarcodePrintHistorySearchReadModel> SearchAsync(
        BarcodePrintHistorySearchCriteria criteria,
        long businessId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Set<BarcodePrintHistory>()
            .AsNoTracking()
            .Include(history => history.Template)
            .Include(history => history.Outlet)
            .Include(history => history.PrintedByUser)
            .Where(history => history.BusinessId == businessId);

        if (criteria.TemplateId.HasValue)
            query = query.Where(history => history.TemplateId == criteria.TemplateId.Value);

        if (criteria.PrintedByUserId.HasValue)
            query = query.Where(history => history.PrintedByUserId == criteria.PrintedByUserId.Value);

        if (criteria.StartDate.HasValue)
            query = query.Where(history => history.PrintedAt >= criteria.StartDate.Value);

        if (criteria.EndDate.HasValue)
            query = query.Where(history => history.PrintedAt <= criteria.EndDate.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(history => history.PrintedAt)
            .Skip((criteria.PageNumber - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return new BarcodePrintHistorySearchReadModel(
            rows.Select(history => ToReadModel(history, includeItems: false)).ToArray(),
            totalCount,
            criteria.PageNumber,
            criteria.PageSize);
    }

    public async Task<BarcodePrintHistoryLookupReadModel> GetByIdAsync(
        long id,
        BarcodePrintHistoryAccessScope accessScope,
        CancellationToken cancellationToken)
    {
        var history = await dbContext.Set<BarcodePrintHistory>()
            .AsNoTracking()
            .Include(candidate => candidate.Template)
            .Include(candidate => candidate.Outlet)
            .Include(candidate => candidate.PrintedByUser)
            .Include(candidate => candidate.Items.OrderBy(item => item.Id))
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (history is null)
            return new BarcodePrintHistoryLookupReadModel(
                BarcodePrintHistoryLookupStatus.NotFound,
                null);

        if (!accessScope.IsSuperAdmin && !accessScope.BusinessId.HasValue)
            return new BarcodePrintHistoryLookupReadModel(
                BarcodePrintHistoryLookupStatus.BusinessScopeRequired,
                null);

        if (!accessScope.IsSuperAdmin && history.BusinessId != accessScope.BusinessId)
            return new BarcodePrintHistoryLookupReadModel(
                BarcodePrintHistoryLookupStatus.Forbidden,
                null);

        return new BarcodePrintHistoryLookupReadModel(
            BarcodePrintHistoryLookupStatus.Found,
            ToReadModel(history, includeItems: true));
    }

    private static BarcodePrintHistoryReadModel ToReadModel(
        BarcodePrintHistory history,
        bool includeItems) =>
        new(
            history.Id,
            history.TemplateId,
            history.Template?.Name,
            history.OutletId,
            history.Outlet?.Name,
            history.PrintedByUserId,
            history.PrintedByUser?.Name,
            history.PrintMode,
            history.LabelWidthMm,
            history.LabelHeightMm,
            history.TotalLabels,
            history.SourceModule,
            history.SourceReferenceType,
            history.SourceReferenceId,
            history.PrintedAt,
            includeItems
                ? history.Items.Select(item => new BarcodePrintHistoryItemReadModel(
                    item.Id,
                    item.VariantId,
                    item.ProductName,
                    item.VariantName,
                    item.VariantSku,
                    item.BarcodeValue,
                    item.VariantAttributes,
                    item.SellingPrice,
                    item.QuantityPrinted)).ToArray()
                : Array.Empty<BarcodePrintHistoryItemReadModel>());
}
