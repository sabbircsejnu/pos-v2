using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

/// <summary>
/// Executes the durable adjustment job created by an approved StockCount. All Inventory, StockLedger,
/// Accounting and StockCount completion effects are produced by the existing StockAdjustment draft builder
/// and approval/posting store inside ONE required-audit PostgreSQL transaction together with the job outcome.
/// Ambiguous movement evidence blocks the job without changing any stock.
/// </summary>
public sealed class StockCountAdjustmentProcessor(
    RetailPOSDbContext dbContext,
    IAuditContext audit,
    IRequiredAuditTransaction auditTransaction,
    IStockCountAdjustmentApproverPolicy approverPolicy,
    StockCountMovementEvidenceService evidenceService,
    StockCountAdjustmentDraftBuilder draftBuilder,
    IStockAdjustmentApprovalStore approvalStore,
    ISystemSettingsStore settings,
    StockCountAdjustmentOptions options,
    TimeProvider clock,
    ILogger<StockCountAdjustmentProcessor> logger) : IStockCountAdjustmentProcessor
{
    private const string WorkerName = "stock-count-adjustment-worker";
    private const int MaximumReasonLength = 500;

    private sealed class BusinessFailureException(string code, Exception inner)
        : Exception(code, inner)
    {
        public string Code { get; } = code;
    }

    private sealed record ClaimedJob(
        Guid Id,
        long StockCountId,
        long BusinessId,
        long LocationId,
        string LocationType,
        long ApproverUserId,
        string ApproverRoleName,
        Guid AuditCorrelationId);

    private sealed record ClaimResult(StockCountAdjustmentProcessingOutcome? Outcome, ClaimedJob? Job);

    public async Task<StockCountAdjustmentProcessingOutcome> ProcessAsync(
        Guid jobId,
        long? expectedBusinessId,
        CancellationToken cancellationToken)
    {
        options.Validate();
        var header = await dbContext.StockCountAdjustmentJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => new { job.BusinessId, job.AuditCorrelationId })
            .SingleOrDefaultAsync(cancellationToken);
        if (header is null || (expectedBusinessId.HasValue && expectedBusinessId != header.BusinessId))
            return StockCountAdjustmentProcessingOutcome.NotFound;

        var owner = Guid.NewGuid();
        var claim = await ClaimAsync(jobId, header.BusinessId, header.AuditCorrelationId, owner, cancellationToken);
        if (claim.Job is null)
            return claim.Outcome!.Value;

        try
        {
            return await ExecuteAsync(claim.Job, owner, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BusinessFailureException failure)
        {
            logger.LogWarning(
                "Stock Count adjustment job {JobId} failed permanently ({Code}); nothing was posted.",
                jobId,
                failure.Code);
            return await RecordOutcomeAsync(claim.Job, owner, failure.Code, terminal: true, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Stock Count adjustment job {JobId} hit a retryable failure ({FailureType}).",
                jobId,
                exception.GetType().Name);
            return await RecordOutcomeAsync(
                claim.Job,
                owner,
                StockCountAdjustmentFailureCodes.TransientFailure,
                terminal: false,
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Guid>> FindRecoverableJobIdsAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pendingCutoff = now.AddSeconds(-options.PendingRecoveryGraceSeconds);
        return await dbContext.StockCountAdjustmentJobs
            .AsNoTracking()
            .Where(job =>
                (job.Status == StockCountAdjustmentJobStatus.RetryScheduled && job.NextAttemptAtUtc <= now)
                || (job.Status == StockCountAdjustmentJobStatus.Processing && job.LeaseExpiresAtUtc <= now)
                || (job.Status == StockCountAdjustmentJobStatus.Pending && job.CreatedAtUtc <= pendingCutoff))
            .OrderBy(job => job.UpdatedAtUtc)
            .Select(job => job.Id)
            .Take(options.SweepBatchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task<ClaimResult> ClaimAsync(
        Guid jobId,
        long businessId,
        Guid correlationId,
        Guid owner,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        audit.Initialize(AuditContext.ForBusinessSystemJob(WorkerName, businessId, correlationId));
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(
                businessId,
                "claim",
                "inventory",
                "Claim Stock Count adjustment job",
                nameof(StockCountAdjustmentJob),
                jobId.ToString()),
            async token =>
            {
                var job = await LockJobAsync(jobId, token);
                if (job is null)
                    return new ClaimResult(StockCountAdjustmentProcessingOutcome.NotFound, null);
                var now = clock.GetUtcNow().UtcDateTime;
                if (StockCountAdjustmentJobStatus.IsTerminal(job.Status))
                    return new ClaimResult(StockCountAdjustmentProcessingOutcome.AlreadyTerminal, null);
                if (job.Status == StockCountAdjustmentJobStatus.Processing
                    && UtcTime.AsUtc(job.LeaseExpiresAtUtc) > now)
                    return new ClaimResult(StockCountAdjustmentProcessingOutcome.ClaimedElsewhere, null);
                if (job.Status == StockCountAdjustmentJobStatus.RetryScheduled
                    && UtcTime.AsUtc(job.NextAttemptAtUtc) > now)
                    return new ClaimResult(StockCountAdjustmentProcessingOutcome.NotDue, null);

                if (job.AttemptCount >= options.MaxAttempts)
                {
                    Complete(job, StockCountAdjustmentJobStatus.Failed,
                        StockCountAdjustmentFailureCodes.AttemptsExhausted, now);
                    return new ClaimResult(StockCountAdjustmentProcessingOutcome.Failed, null);
                }

                job.Status = StockCountAdjustmentJobStatus.Processing;
                job.LeaseOwner = owner;
                job.LeaseExpiresAtUtc = now.AddSeconds(options.LeaseSeconds);
                job.AttemptCount++;
                job.UpdatedAtUtc = now;
                job.Version++;
                return new ClaimResult(null, new ClaimedJob(
                    job.Id,
                    job.StockCountId,
                    job.BusinessId,
                    job.LocationId,
                    job.LocationType,
                    job.ApproverUserId,
                    job.ApproverRoleName,
                    job.AuditCorrelationId));
            },
            result => result.Job is not null
                || result.Outcome == StockCountAdjustmentProcessingOutcome.Failed,
            cancellationToken);
    }

    private async Task<StockCountAdjustmentProcessingOutcome> ExecuteAsync(
        ClaimedJob claimed,
        Guid owner,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        // Audit attribution: the persisted StockCount approver is the real actor, the source is the
        // integration worker identified by the request path, and the approval's correlation id is kept.
        audit.Initialize(new AuditContextSnapshot(
            claimed.AuditCorrelationId,
            claimed.ApproverUserId,
            claimed.ApproverUserId,
            null,
            null,
            claimed.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase) ? claimed.LocationId : null,
            claimed.BusinessId,
            AuditSource.Integration,
            null,
            $"job:{WorkerName}",
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            claimed.ApproverRoleName));

        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(
                claimed.BusinessId,
                "adjust",
                "inventory",
                "Post the stock adjustment generated from an approved StockCount",
                nameof(StockCount),
                claimed.StockCountId.ToString()),
            token => PostAsync(claimed, owner, token),
            outcome => outcome != StockCountAdjustmentProcessingOutcome.ClaimedElsewhere,
            cancellationToken);
    }

    private async Task<StockCountAdjustmentProcessingOutcome> PostAsync(
        ClaimedJob claimed,
        Guid owner,
        CancellationToken token)
    {
        var job = await LockJobAsync(claimed.Id, token);
        var now = clock.GetUtcNow().UtcDateTime;
        if (job is null
            || job.Status != StockCountAdjustmentJobStatus.Processing
            || job.LeaseOwner != owner)
        {
            return StockCountAdjustmentProcessingOutcome.ClaimedElsewhere;
        }

        if (job.SubmittedByUserId == job.ApproverUserId)
            return Fail(job, StockCountAdjustmentFailureCodes.MakerCheckerViolation, now);

        var authorization = await approverPolicy.AuthorizeAsync(
            job.ApproverUserId, job.BusinessId, job.LocationId, job.LocationType, token);
        if (authorization is null)
            return Fail(job, StockCountAdjustmentFailureCodes.ApproverNotAuthorized, now);

        var stockCount = await dbContext.StockCounts
            .FromSqlInterpolated($"SELECT * FROM stock_counts WHERE id = {job.StockCountId} FOR UPDATE")
            .SingleOrDefaultAsync(token);
        if (stockCount is null
            || stockCount.BusinessId != job.BusinessId
            || stockCount.LocationId != job.LocationId
            || stockCount.LocationType != job.LocationType)
        {
            return Fail(job, StockCountAdjustmentFailureCodes.CountNotFound, now);
        }

        if (!string.Equals(stockCount.Status, StockCount.StatusApproved, StringComparison.Ordinal))
            return Fail(job, StockCountAdjustmentFailureCodes.CountStateChanged, now);
        if (stockCount.CountRevision != job.ApprovedCountRevision)
            return Fail(job, StockCountAdjustmentFailureCodes.RevisionChanged, now);
        if (await dbContext.StockAdjustments.AsNoTracking()
                .AnyAsync(adjustment => adjustment.SourceStockCountId == stockCount.Id, token))
        {
            return Fail(job, StockCountAdjustmentFailureCodes.AdjustmentAlreadyExists, now);
        }

        await dbContext.Entry(stockCount).Collection(candidate => candidate.Lines).LoadAsync(token);
        var evidence = await evidenceService.EvaluateAsync(stockCount, token);
        var (availability, reasons) = MobileStockTakeResultsPolicy.Decide(
            stockCount.Status,
            stockCount.ApprovedAt,
            evidence.Evaluation,
            evidence.TotalLines,
            evidence.CountedLines,
            evidence.InconsistentDifferenceLines);
        job.EvidenceStatus = evidence.Evaluation.Status;
        if (availability != MobileStockTakeResultsAvailability.Available)
        {
            Block(job, reasons, now);
            return StockCountAdjustmentProcessingOutcome.Blocked;
        }

        long? adjustmentId = null;
        stockCount.MovementReconciliationStatus = StockCount.MovementReconciliationVerified;
        if (!StockCountAdjustmentDraftBuilder.HasDifferences(stockCount))
        {
            // Nothing to adjust: the count is complete without creating an empty adjustment.
            stockCount.Status = StockCount.StatusCompleted;
        }
        else
        {
            try
            {
                var adjustment = await draftBuilder.StageDraftAsync(
                    stockCount,
                    job.SubmittedByUserId ?? job.ApproverUserId,
                    token);
                adjustment.Status = StockAdjustment.StatusPendingApproval;
                adjustment.SubmittedAt = now;
                adjustment.UpdatedAt = now;
                await dbContext.SaveChangesAsync(token);

                var inventorySettings = (await settings.GetAsync(token)).Inventory;
                var context = new StockAdjustmentCreateContext(
                    new StockAdjustmentReadScope(job.BusinessId, authorization.Locations),
                    inventorySettings.AllowNegativeStock);
                // Existing posting logic: Inventory, StockLedger, Accounting and StockCount.Completed.
                await approvalStore.ApproveAsync(adjustment.Id, job.ApproverUserId, context, token);
                adjustmentId = adjustment.Id;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException)
            {
                throw new BusinessFailureException(
                    StockCountAdjustmentFailureCodes.AdjustmentValidationFailed,
                    exception);
            }
        }

        job.StockAdjustmentId = adjustmentId;
        Complete(job, StockCountAdjustmentJobStatus.Completed, null, now);
        return StockCountAdjustmentProcessingOutcome.Completed;
    }

    private async Task<StockCountAdjustmentProcessingOutcome> RecordOutcomeAsync(
        ClaimedJob claimed,
        Guid owner,
        string code,
        bool terminal,
        CancellationToken cancellationToken)
    {
        try
        {
            dbContext.ChangeTracker.Clear();
            audit.Initialize(AuditContext.ForBusinessSystemJob(
                WorkerName,
                claimed.BusinessId,
                claimed.AuditCorrelationId));
            return await auditTransaction.ExecuteAsync(
                new RequiredAuditOperation(
                    claimed.BusinessId,
                    terminal ? "fail" : "retry",
                    "inventory",
                    terminal
                        ? "Record permanent Stock Count adjustment failure"
                        : "Record retryable Stock Count adjustment failure",
                    nameof(StockCountAdjustmentJob),
                    claimed.Id.ToString()),
                async token =>
                {
                    var job = await LockJobAsync(claimed.Id, token);
                    if (job is null
                        || job.Status != StockCountAdjustmentJobStatus.Processing
                        || job.LeaseOwner != owner)
                    {
                        return StockCountAdjustmentProcessingOutcome.ClaimedElsewhere;
                    }

                    var now = clock.GetUtcNow().UtcDateTime;
                    if (terminal)
                        return Fail(job, code, now);
                    if (job.AttemptCount >= options.MaxAttempts)
                        return Fail(job, StockCountAdjustmentFailureCodes.AttemptsExhausted, now);

                    var delay = Math.Min(
                        options.MaxRetrySeconds,
                        options.BaseRetrySeconds * Math.Pow(2, Math.Min(job.AttemptCount - 1, 20)));
                    job.Status = StockCountAdjustmentJobStatus.RetryScheduled;
                    job.FailureCode = StockCountAdjustmentFailureCodes.TransientFailure;
                    job.NextAttemptAtUtc = now.AddSeconds(delay);
                    job.LeaseOwner = null;
                    job.LeaseExpiresAtUtc = null;
                    job.UpdatedAtUtc = now;
                    job.Version++;
                    return StockCountAdjustmentProcessingOutcome.RetryScheduled;
                },
                outcome => outcome != StockCountAdjustmentProcessingOutcome.ClaimedElsewhere,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The expiring processing lease keeps the job recoverable by the sweeper.
            logger.LogError(
                "Could not persist outcome for Stock Count adjustment job {JobId} ({FailureType}); lease expiry will recover it.",
                claimed.Id,
                exception.GetType().Name);
            return StockCountAdjustmentProcessingOutcome.RetryScheduled;
        }
    }

    private Task<StockCountAdjustmentJob?> LockJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        dbContext.StockCountAdjustmentJobs
            .FromSqlInterpolated(
                $"SELECT * FROM stock_count_adjustment_jobs WHERE id = {jobId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static StockCountAdjustmentProcessingOutcome Fail(
        StockCountAdjustmentJob job,
        string failureCode,
        DateTime now)
    {
        Complete(job, StockCountAdjustmentJobStatus.Failed, failureCode, now);
        return StockCountAdjustmentProcessingOutcome.Failed;
    }

    private static void Block(StockCountAdjustmentJob job, IReadOnlyList<string> reasons, DateTime now)
    {
        var codes = string.Join(',', reasons.Distinct());
        job.BlockedReasonCodes = codes.Length <= MaximumReasonLength ? codes : codes[..MaximumReasonLength];
        Complete(job, StockCountAdjustmentJobStatus.Blocked, null, now);
    }

    private static void Complete(
        StockCountAdjustmentJob job,
        string status,
        string? failureCode,
        DateTime now)
    {
        job.Status = status;
        job.FailureCode = failureCode;
        job.NextAttemptAtUtc = null;
        job.LeaseOwner = null;
        job.LeaseExpiresAtUtc = null;
        job.CompletedAtUtc = now;
        job.UpdatedAtUtc = now;
        job.Version++;
    }
}
