using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Application.Inventory.StockCounts.Commands;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

/// <summary>
/// Executes Mobile Stock Take submission jobs. It only invokes the existing ERP StockCount submission
/// command with the persisted initiating actor; it never approves, rejects, adjusts or posts stock.
/// </summary>
public sealed class MobileStockTakeSubmissionProcessor(
    RetailPOSDbContext dbContext,
    IAuditContext audit,
    IRequiredAuditTransaction auditTransaction,
    ISender sender,
    IMobileStockTakeSubmissionActorPolicy actorPolicy,
    MobileStockTakeSubmissionOptions options,
    TimeProvider clock,
    ILogger<MobileStockTakeSubmissionProcessor> logger) : IMobileStockTakeSubmissionProcessor
{
    private const string WorkerName = "mobile-stock-take-submission-worker";

    private sealed record ClaimedJob(
        Guid Id,
        long StockCountId,
        long BusinessId,
        long OutletId,
        long ActorUserId,
        string ActorRoleName,
        long AcceptedCountRevision,
        Guid AuditCorrelationId);

    private sealed record ClaimResult(
        MobileStockTakeSubmissionProcessingOutcome? Outcome,
        ClaimedJob? Job);

    public async Task<MobileStockTakeSubmissionProcessingOutcome> ProcessAsync(
        Guid jobId,
        long? expectedBusinessId,
        CancellationToken cancellationToken)
    {
        options.Validate();
        var header = await dbContext.StockCountSubmissionJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => new { job.BusinessId, job.AuditCorrelationId })
            .SingleOrDefaultAsync(cancellationToken);
        if (header is null || (expectedBusinessId.HasValue && expectedBusinessId != header.BusinessId))
            return MobileStockTakeSubmissionProcessingOutcome.NotFound;

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
        catch (Exception exception)
        {
            logger.LogWarning(
                "Mobile Stock Take submission job {JobId} hit a retryable failure ({FailureType}).",
                jobId,
                exception.GetType().Name);
            return await RecordTransientFailureAsync(claim.Job, owner, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Guid>> FindRecoverableJobIdsAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var acceptedCutoff = now.AddSeconds(-options.AcceptedRecoveryGraceSeconds);
        return await dbContext.StockCountSubmissionJobs
            .AsNoTracking()
            .Where(job =>
                (job.Status == StockCountSubmissionJobStatus.RetryScheduled && job.NextAttemptAtUtc <= now)
                || (job.Status == StockCountSubmissionJobStatus.Processing && job.LeaseExpiresAtUtc <= now)
                || (job.Status == StockCountSubmissionJobStatus.Accepted && job.CreatedAtUtc <= acceptedCutoff))
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
                "Claim Mobile Stock Take submission job",
                nameof(StockCountSubmissionJob),
                jobId.ToString()),
            async token =>
            {
                var job = await LockJobAsync(jobId, token);
                if (job is null)
                    return new ClaimResult(MobileStockTakeSubmissionProcessingOutcome.NotFound, null);
                var now = clock.GetUtcNow().UtcDateTime;
                if (StockCountSubmissionJobStatus.IsTerminal(job.Status))
                    return new ClaimResult(MobileStockTakeSubmissionProcessingOutcome.AlreadyTerminal, null);
                if (job.Status == StockCountSubmissionJobStatus.Processing
                    && UtcTime.AsUtc(job.LeaseExpiresAtUtc) > now)
                    return new ClaimResult(MobileStockTakeSubmissionProcessingOutcome.ClaimedElsewhere, null);
                if (job.Status == StockCountSubmissionJobStatus.RetryScheduled
                    && UtcTime.AsUtc(job.NextAttemptAtUtc) > now)
                    return new ClaimResult(MobileStockTakeSubmissionProcessingOutcome.NotDue, null);

                if (job.AttemptCount >= options.MaxAttempts)
                {
                    Complete(job, StockCountSubmissionJobStatus.Failed,
                        MobileStockTakeSubmissionFailureCodes.AttemptsExhausted, now);
                    return new ClaimResult(MobileStockTakeSubmissionProcessingOutcome.Failed, null);
                }

                job.Status = StockCountSubmissionJobStatus.Processing;
                job.LeaseOwner = owner;
                job.LeaseExpiresAtUtc = now.AddSeconds(options.LeaseSeconds);
                job.AttemptCount++;
                job.UpdatedAtUtc = now;
                job.Version++;
                return new ClaimResult(null, new ClaimedJob(
                    job.Id,
                    job.StockCountId,
                    job.BusinessId,
                    job.OutletId,
                    job.ActorUserId,
                    job.ActorRoleName,
                    job.AcceptedCountRevision,
                    job.AuditCorrelationId));
            },
            result => result.Job is not null
                || result.Outcome == MobileStockTakeSubmissionProcessingOutcome.Failed,
            cancellationToken);
    }

    private async Task<MobileStockTakeSubmissionProcessingOutcome> ExecuteAsync(
        ClaimedJob claimed,
        Guid owner,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        // The persisted initiating actor stays the audit identity; no privileged identity is substituted.
        audit.Initialize(new AuditContextSnapshot(
            claimed.AuditCorrelationId,
            claimed.ActorUserId,
            claimed.ActorUserId,
            null,
            null,
            claimed.OutletId,
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
            claimed.ActorRoleName));

        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(
                claimed.BusinessId,
                "submit",
                "inventory",
                "Submit Mobile Stock Take count to the existing ERP StockCount workflow",
                nameof(StockCount),
                claimed.StockCountId.ToString()),
            async token =>
            {
                var job = await LockJobAsync(claimed.Id, token);
                var now = clock.GetUtcNow().UtcDateTime;
                if (job is null
                    || job.Status != StockCountSubmissionJobStatus.Processing
                    || job.LeaseOwner != owner)
                {
                    return MobileStockTakeSubmissionProcessingOutcome.ClaimedElsewhere;
                }

                if (!await actorPolicy.IsStillAuthorizedAsync(
                        new MobileStockTakeSubmissionActorContext(
                            claimed.ActorUserId,
                            claimed.BusinessId,
                            claimed.OutletId),
                        token))
                {
                    return Fail(job, MobileStockTakeSubmissionFailureCodes.ActorNotAuthorized, now);
                }

                var session = await dbContext.StockCounts
                    .FromSqlInterpolated(
                        $"SELECT * FROM stock_counts WHERE id = {claimed.StockCountId} FOR UPDATE")
                    .SingleOrDefaultAsync(token);
                if (session is null
                    || session.BusinessId != claimed.BusinessId
                    || session.LocationId != claimed.OutletId
                    || session.LocationType != "outlet")
                {
                    return Fail(job, MobileStockTakeSubmissionFailureCodes.SessionNotFound, now);
                }

                if (!string.Equals(session.Status, StockCount.StatusDraft, StringComparison.Ordinal))
                    return Fail(job, MobileStockTakeSubmissionFailureCodes.SessionStateChanged, now);
                if (session.CountRevision != claimed.AcceptedCountRevision)
                    return Fail(job, MobileStockTakeSubmissionFailureCodes.RevisionChanged, now);

                try
                {
                    await sender.Send(
                        new SubmitStockCountCommand(
                            session.Id,
                            session.BusinessId,
                            session.LocationId,
                            session.LocationType,
                            claimed.ActorUserId),
                        token);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
                {
                    // The ERP command validates before mutating, so no partial StockCount change is staged.
                    return Fail(job, MobileStockTakeSubmissionFailureCodes.ErpSubmissionRejected, now);
                }

                Complete(job, StockCountSubmissionJobStatus.ErpSubmitted, null, now);
                return MobileStockTakeSubmissionProcessingOutcome.ErpSubmitted;
            },
            outcome => outcome != MobileStockTakeSubmissionProcessingOutcome.ClaimedElsewhere,
            cancellationToken);
    }

    private async Task<MobileStockTakeSubmissionProcessingOutcome> RecordTransientFailureAsync(
        ClaimedJob claimed,
        Guid owner,
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
                    "retry",
                    "inventory",
                    "Record retryable Mobile Stock Take submission failure",
                    nameof(StockCountSubmissionJob),
                    claimed.Id.ToString()),
                async token =>
                {
                    var job = await LockJobAsync(claimed.Id, token);
                    if (job is null
                        || job.Status != StockCountSubmissionJobStatus.Processing
                        || job.LeaseOwner != owner)
                    {
                        return MobileStockTakeSubmissionProcessingOutcome.ClaimedElsewhere;
                    }

                    var now = clock.GetUtcNow().UtcDateTime;
                    if (job.AttemptCount >= options.MaxAttempts)
                    {
                        return Fail(job, MobileStockTakeSubmissionFailureCodes.AttemptsExhausted, now);
                    }

                    var delay = Math.Min(
                        options.MaxRetrySeconds,
                        options.BaseRetrySeconds * Math.Pow(2, Math.Min(job.AttemptCount - 1, 20)));
                    job.Status = StockCountSubmissionJobStatus.RetryScheduled;
                    job.FailureCode = MobileStockTakeSubmissionFailureCodes.TransientFailure;
                    job.NextAttemptAtUtc = now.AddSeconds(delay);
                    job.LeaseOwner = null;
                    job.LeaseExpiresAtUtc = null;
                    job.UpdatedAtUtc = now;
                    job.Version++;
                    return MobileStockTakeSubmissionProcessingOutcome.RetryScheduled;
                },
                outcome => outcome != MobileStockTakeSubmissionProcessingOutcome.ClaimedElsewhere,
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
                "Could not persist retry state for Mobile Stock Take submission job {JobId} ({FailureType}); lease expiry will recover it.",
                claimed.Id,
                exception.GetType().Name);
            return MobileStockTakeSubmissionProcessingOutcome.RetryScheduled;
        }
    }

    private Task<StockCountSubmissionJob?> LockJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        dbContext.StockCountSubmissionJobs
            .FromSqlInterpolated(
                $"SELECT * FROM stock_count_submission_jobs WHERE id = {jobId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static MobileStockTakeSubmissionProcessingOutcome Fail(
        StockCountSubmissionJob job,
        string failureCode,
        DateTime now)
    {
        Complete(job, StockCountSubmissionJobStatus.Failed, failureCode, now);
        return MobileStockTakeSubmissionProcessingOutcome.Failed;
    }

    private static void Complete(
        StockCountSubmissionJob job,
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
