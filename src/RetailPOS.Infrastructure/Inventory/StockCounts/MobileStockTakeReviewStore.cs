using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public sealed class MobileStockTakeReviewStore(
    RetailPOSDbContext dbContext,
    StockCountMovementEvidenceService evidenceService,
    IStockCountAsyncAdjustmentSwitch asyncAdjustment) : IMobileStockTakeReviewStore
{
    private const string OutletLocationType = "outlet";

    public async Task<MobileStockTakeHistoryPageReadModel> SearchHistoryAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId,
        MobileStockTakeHistoryFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = AuthorizedSessions(businessId, authorizedOutletIds, ownerUserId);
        if (filter.OutletId.HasValue)
            query = query.Where(session => session.LocationId == filter.OutletId.Value);
        if (filter.Status is not null)
            query = query.Where(session => session.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(session =>
                session.StockCountNo.Contains(term)
                || (session.Remarks != null && session.Remarks.Contains(term)));
        }

        if (filter.CreatedFromUtc.HasValue)
            query = query.Where(session => session.CreatedAt >= filter.CreatedFromUtc.Value);
        if (filter.CreatedToUtc.HasValue)
            query = query.Where(session => session.CreatedAt <= filter.CreatedToUtc.Value);
        if (filter.SubmissionStatus is not null)
        {
            var submissionStatus = filter.SubmissionStatus;
            query = query.Where(session => dbContext.StockCountSubmissionJobs
                .Where(job => job.StockCountId == session.Id)
                .OrderByDescending(job => job.CreatedAtUtc)
                .ThenByDescending(job => job.Id)
                .Select(job => job.Status)
                .FirstOrDefault() == submissionStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(session => session.CreatedAt)
            .ThenByDescending(session => session.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(session => new
            {
                session.Id,
                session.StockCountNo,
                session.LocationId,
                OutletName = dbContext.Outlets
                    .Where(outlet => outlet.Id == session.LocationId && outlet.BusinessId == businessId)
                    .Select(outlet => outlet.Name)
                    .First(),
                session.StockCountDate,
                session.Status,
                session.CountRevision,
                session.TotalItems,
                session.CreatedAt,
                session.SubmittedAt,
                session.ApprovedAt,
                session.RejectedAt,
                session.MovementReconciliationStatus,
                Job = dbContext.StockCountSubmissionJobs
                    .Where(job => job.StockCountId == session.Id)
                    .OrderByDescending(job => job.CreatedAtUtc)
                    .ThenByDescending(job => job.Id)
                    .Select(job => new
                    {
                        job.Id,
                        job.Status,
                        job.AttemptCount,
                        job.FailureCode,
                        job.AcceptedCountRevision,
                        job.CreatedAtUtc,
                        job.UpdatedAtUtc,
                        job.CompletedAtUtc
                    })
                    .FirstOrDefault()
            })
            .ToArrayAsync(cancellationToken);

        var items = rows.Select(row => new MobileStockTakeHistoryItemReadModel(
            row.Id,
            row.StockCountNo,
            row.LocationId,
            row.OutletName,
            row.StockCountDate,
            row.Status,
            MobileStockTakeLifecycleStageResolver.Resolve(
                row.Status,
                row.CountRevision,
                row.Job?.Status,
                row.Job?.AcceptedCountRevision),
            row.CountRevision,
            row.TotalItems,
            UtcTime.AsUtc(row.CreatedAt),
            UtcTime.AsUtc(row.SubmittedAt),
            UtcTime.AsUtc(row.ApprovedAt),
            UtcTime.AsUtc(row.RejectedAt),
            row.MovementReconciliationStatus,
            row.Job is null
                ? null
                : new MobileStockTakeLatestSubmissionReadModel(
                    row.Job.Id,
                    row.Job.Status,
                    StockCountSubmissionJobStatus.IsTerminal(row.Job.Status),
                    row.Job.AttemptCount,
                    row.Job.FailureCode,
                    UtcTime.AsUtc(row.Job.CreatedAtUtc),
                    UtcTime.AsUtc(row.Job.UpdatedAtUtc),
                    UtcTime.AsUtc(row.Job.CompletedAtUtc)))).ToArray();
        return new MobileStockTakeHistoryPageReadModel(items, page, pageSize, totalCount);
    }

    public async Task<MobileStockTakeLifecycleReadModel?> GetLifecycleAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId,
        long stockCountId,
        CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(
            businessId, authorizedOutletIds, ownerUserId, stockCountId, cancellationToken);
        if (session is null)
            return null;

        var outletName = await dbContext.Outlets.AsNoTracking()
            .Where(outlet => outlet.Id == session.LocationId && outlet.BusinessId == businessId)
            .Select(outlet => outlet.Name)
            .FirstAsync(cancellationToken);
        var jobs = dbContext.StockCountSubmissionJobs.AsNoTracking()
            .Where(job => job.StockCountId == stockCountId);
        var jobCount = await jobs.CountAsync(cancellationToken);
        var latestJob = await jobs
            .OrderByDescending(job => job.CreatedAtUtc)
            .ThenByDescending(job => job.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var adjustmentStatus = await dbContext.StockAdjustments.AsNoTracking()
            .Where(adjustment => adjustment.SourceStockCountId == stockCountId)
            .OrderByDescending(adjustment => adjustment.Id)
            .Select(adjustment => adjustment.Status)
            .FirstOrDefaultAsync(cancellationToken);

        var adjustmentJob = asyncAdjustment.Enabled
            ? await dbContext.StockCountAdjustmentJobs.AsNoTracking()
                .Where(job => job.StockCountId == stockCountId)
                .OrderByDescending(job => job.CreatedAtUtc)
                .ThenByDescending(job => job.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var evidence = await EvaluateAsync(session, cancellationToken);
        var (availability, reasons) = MobileStockTakeResultsPolicy.Decide(
            session.Status,
            UtcTime.AsUtc(session.ApprovedAt),
            evidence.Evaluation,
            evidence.TotalLines,
            evidence.CountedLines,
            evidence.InconsistentDifferenceLines);

        return new MobileStockTakeLifecycleReadModel(
            session.Id,
            session.StockCountNo,
            session.LocationId,
            outletName,
            session.Status,
            MobileStockTakeLifecycleStageResolver.Resolve(
                session.Status,
                session.CountRevision,
                latestJob?.Status,
                latestJob?.AcceptedCountRevision),
            session.CountRevision,
            UtcTime.AsUtc(session.CreatedAt),
            UtcTime.AsUtc(session.SubmittedAt),
            UtcTime.AsUtc(session.ApprovedAt),
            UtcTime.AsUtc(session.RejectedAt),
            !string.IsNullOrWhiteSpace(session.RejectionReason),
            jobCount,
            latestJob is null ? null : MobileStockTakeSubmissionStore.ToReadModel(latestJob, session),
            session.MovementReconciliationStatus,
            evidence.Evaluation,
            adjustmentStatus ?? MobileStockTakeAdjustmentStates.NotGenerated,
            adjustmentStatus is StockAdjustment.StatusApproved or StockAdjustment.StatusReversed,
            adjustmentJob is null
                && session.Status == StockCount.StatusApproved
                && session.MovementReconciliationStatus == StockCount.MovementReconciliationUnresolved,
            availability,
            reasons,
            adjustmentJob is null ? null : StockCountAdjustmentJobStore.ToReadModel(adjustmentJob));
    }

    public async Task<MobileStockTakeSubmissionPageReadModel?> GetSubmissionsAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId,
        long stockCountId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(
            businessId, authorizedOutletIds, ownerUserId, stockCountId, cancellationToken);
        if (session is null)
            return null;

        var query = dbContext.StockCountSubmissionJobs.AsNoTracking()
            .Where(job => job.StockCountId == stockCountId
                          && job.BusinessId == businessId
                          && job.OutletId == session.LocationId);
        var totalCount = await query.CountAsync(cancellationToken);
        var jobs = await query
            .OrderByDescending(job => job.CreatedAtUtc)
            .ThenByDescending(job => job.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return new MobileStockTakeSubmissionPageReadModel(
            jobs.Select(job => MobileStockTakeSubmissionStore.ToReadModel(job, session)).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<MobileStockTakeResultsReadModel?> GetResultsAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId,
        long stockCountId,
        bool differencesOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(
            businessId, authorizedOutletIds, ownerUserId, stockCountId, cancellationToken);
        if (session is null)
            return null;

        // Blind counting: nothing about stock or variance is read before ERP approval.
        if (!MobileStockTakeResultsPolicy.IsApprovedState(session.Status))
        {
            return new MobileStockTakeResultsReadModel(
                session.Id,
                session.Status,
                MobileStockTakeResultsAvailability.NotAvailable,
                [MobileStockTakeResultsReasonCodes.NotApproved],
                null,
                null,
                null,
                null,
                null);
        }

        var evidence = await EvaluateAsync(session, cancellationToken);
        var approvedAt = UtcTime.AsUtc(session.ApprovedAt);
        var (availability, reasons) = MobileStockTakeResultsPolicy.Decide(
            session.Status,
            approvedAt,
            evidence.Evaluation,
            evidence.TotalLines,
            evidence.CountedLines,
            evidence.InconsistentDifferenceLines);
        if (availability != MobileStockTakeResultsAvailability.Available)
        {
            return new MobileStockTakeResultsReadModel(
                session.Id, session.Status, availability, reasons, approvedAt, null, null, null, null);
        }

        var lines = dbContext.StockCountLines.AsNoTracking()
            .Where(line => line.StockCountId == stockCountId);
        var differenceLines = await lines.CountAsync(line => line.Difference != 0, cancellationToken);
        var netDifference = await lines.SumAsync(line => line.Difference ?? 0m, cancellationToken);
        var pageQuery = differencesOnly ? lines.Where(line => line.Difference != 0) : lines;
        var totalCount = await pageQuery.CountAsync(cancellationToken);
        var items = await pageQuery
            .OrderBy(line => line.ProductName)
            .ThenBy(line => line.VariantName)
            .ThenBy(line => line.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(line => new MobileStockTakeResultLineReadModel(
                line.Id,
                line.ProductId,
                line.VariantId,
                line.ProductName,
                line.ProductCode,
                line.VariantName,
                line.PhysicalStock!.Value,
                line.CurrentStock,
                line.PhysicalStock!.Value - line.CurrentStock))
            .ToArrayAsync(cancellationToken);

        return new MobileStockTakeResultsReadModel(
            session.Id,
            session.Status,
            MobileStockTakeResultsAvailability.Available,
            [],
            approvedAt,
            evidence.CountedLines,
            differenceLines,
            netDifference,
            new MobileStockTakeResultLinePageReadModel(items, page, pageSize, totalCount));
    }

    private Task<StockCountMovementEvidence> EvaluateAsync(
        StockCount session,
        CancellationToken cancellationToken) =>
        evidenceService.EvaluateAsync(session, cancellationToken);

    private async Task<StockCount?> FindSessionAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId,
        long stockCountId,
        CancellationToken cancellationToken) =>
        await AuthorizedSessions(businessId, authorizedOutletIds, ownerUserId)
            .FirstOrDefaultAsync(session => session.Id == stockCountId, cancellationToken);

    private IQueryable<StockCount> AuthorizedSessions(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId) =>
        dbContext.StockCounts
            .AsNoTracking()
            .Where(stockCount =>
                stockCount.BusinessId == businessId
                && stockCount.LocationType == OutletLocationType
                && authorizedOutletIds.Contains(stockCount.LocationId)
                && dbContext.Outlets.Any(outlet =>
                    outlet.Id == stockCount.LocationId
                    && outlet.BusinessId == businessId)
                && (ownerUserId == null
                    || stockCount.CreatedBy == ownerUserId
                    || stockCount.SubmittedBy == ownerUserId));
}
