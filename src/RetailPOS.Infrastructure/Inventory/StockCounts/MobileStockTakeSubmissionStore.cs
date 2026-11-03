using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public static class MobileStockTakeSubmissionMessage
{
    public const string Producer = "RetailPOS";
    public const string MessageType = "mobile.stock-take.submission.requested";
    public const string AggregateType = "StockCountSubmissionJob";
    public const int SchemaVersion = 1;

    public sealed record Payload(long BusinessId, Guid JobId, long StockCountId, long AcceptedCountRevision);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(Payload payload) => JsonSerializer.Serialize(payload, JsonOptions);

    public static Payload? Deserialize(string json) => JsonSerializer.Deserialize<Payload>(json, JsonOptions);
}

public sealed class MobileStockTakeSubmissionStore(
    RetailPOSDbContext dbContext,
    IRequiredAuditTransaction auditTransaction,
    IIntegrationOutbox outbox,
    IAuditContext audit,
    TimeProvider clock) : IMobileStockTakeSubmissionStore
{
    public Task<AcceptMobileStockTakeSubmissionResult> AcceptAsync(
        AcceptMobileStockTakeSubmissionInput input,
        CancellationToken cancellationToken) =>
        auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(
                input.BusinessId,
                "submit",
                "inventory",
                "Accept Mobile Stock Take submission for asynchronous ERP submission",
                "StockCount",
                input.StockCountId.ToString()),
            token => AcceptCoreAsync(input, token),
            result => result.ErrorCode is null && !result.IsReplay,
            cancellationToken);

    private async Task<AcceptMobileStockTakeSubmissionResult> AcceptCoreAsync(
        AcceptMobileStockTakeSubmissionInput input,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.StockCounts
            .FromSqlInterpolated(
                $"SELECT * FROM stock_counts WHERE id = {input.StockCountId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null
            || session.BusinessId != input.BusinessId
            || session.LocationId != input.OutletId
            || session.LocationType != "outlet")
        {
            return AcceptMobileStockTakeSubmissionResult.Error(
                MobileStockTakeSubmissionErrorCodes.NotFound,
                session?.CountRevision ?? 0);
        }

        var fingerprint = ComputeFingerprint(input.StockCountId, input.AcceptedCountRevision);
        var sameKey = await dbContext.StockCountSubmissionJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                job => job.StockCountId == session.Id && job.IdempotencyKey == input.IdempotencyKey,
                cancellationToken);
        if (sameKey is not null)
        {
            return string.Equals(sameKey.RequestFingerprint, fingerprint, StringComparison.Ordinal)
                ? new AcceptMobileStockTakeSubmissionResult(
                    null,
                    session.CountRevision,
                    ToReadModel(sameKey, session),
                    IsReplay: true)
                : AcceptMobileStockTakeSubmissionResult.Error(
                    MobileStockTakeSubmissionErrorCodes.IdempotencyMismatch,
                    session.CountRevision);
        }

        var active = await dbContext.StockCountSubmissionJobs
            .AsNoTracking()
            .Where(job => job.StockCountId == session.Id
                && Enumerable.Contains(StockCountSubmissionJobStatus.Active, job.Status))
            .SingleOrDefaultAsync(cancellationToken);
        if (active is not null)
        {
            return AcceptMobileStockTakeSubmissionResult.Error(
                MobileStockTakeSubmissionErrorCodes.InProgress,
                session.CountRevision,
                ToReadModel(active, session));
        }

        if (!string.Equals(session.Status, StockCount.StatusDraft, StringComparison.Ordinal))
        {
            return AcceptMobileStockTakeSubmissionResult.Error(
                MobileStockTakeSubmissionErrorCodes.NotDraft,
                session.CountRevision);
        }

        if (session.CountRevision <= 0)
        {
            return AcceptMobileStockTakeSubmissionResult.Error(
                MobileStockTakeSubmissionErrorCodes.NoAcceptedCounts,
                session.CountRevision);
        }

        if (input.AcceptedCountRevision != session.CountRevision)
        {
            return AcceptMobileStockTakeSubmissionResult.Error(
                MobileStockTakeSubmissionErrorCodes.StaleRevision,
                session.CountRevision);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var job = new StockCountSubmissionJob
        {
            Id = Guid.NewGuid(),
            StockCountId = session.Id,
            BusinessId = session.BusinessId,
            OutletId = session.LocationId,
            ActorUserId = input.ActorUserId,
            ActorRoleName = input.ActorRoleName,
            IdempotencyKey = input.IdempotencyKey,
            RequestFingerprint = fingerprint,
            AcceptedCountRevision = session.CountRevision,
            Status = StockCountSubmissionJobStatus.Accepted,
            NextAttemptAtUtc = now,
            OutboxMessageId = Guid.NewGuid(),
            AuditCorrelationId = audit.CorrelationId,
            RequestCorrelationId = input.RequestCorrelationId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1
        };
        dbContext.StockCountSubmissionJobs.Add(job);

        var payload = MobileStockTakeSubmissionMessage.Serialize(new MobileStockTakeSubmissionMessage.Payload(
            job.BusinessId,
            job.Id,
            job.StockCountId,
            job.AcceptedCountRevision));
        await outbox.AppendAsync(
            new IntegrationEnvelope(
                job.OutboxMessageId,
                job.BusinessId,
                MobileStockTakeSubmissionMessage.Producer,
                MobileStockTakeSubmissionMessage.MessageType,
                MobileStockTakeSubmissionMessage.SchemaVersion,
                MobileStockTakeSubmissionMessage.AggregateType,
                job.Id.ToString(),
                audit.CorrelationId,
                null,
                new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc),
                payload,
                IntegrationEnvelope.Hash(payload)),
            cancellationToken);

        return new AcceptMobileStockTakeSubmissionResult(
            null,
            session.CountRevision,
            ToReadModel(job, session),
            IsReplay: false);
    }

    public async Task<MobileStockTakeSubmissionReadModel?> FindAsync(
        long businessId,
        long outletId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var row = await (
            from job in dbContext.StockCountSubmissionJobs.AsNoTracking()
            join session in dbContext.StockCounts.AsNoTracking()
                on job.StockCountId equals session.Id
            where job.Id == jobId
                && session.Id == stockCountId
                && session.BusinessId == businessId
                && session.LocationId == outletId
                && session.LocationType == "outlet"
                && Enumerable.Contains(authorizedOutletIds, session.LocationId)
                && dbContext.Outlets.Any(outlet =>
                    outlet.Id == session.LocationId && outlet.BusinessId == businessId)
            select new { Job = job, Session = session })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToReadModel(row.Job, row.Session);
    }

    internal static string ComputeFingerprint(long stockCountId, long acceptedRevision) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"mobile-stock-take-submission:v1|{stockCountId}|{acceptedRevision}")));

    internal static MobileStockTakeSubmissionReadModel ToReadModel(
        StockCountSubmissionJob job,
        StockCount session) =>
        new(
            job.Id,
            job.StockCountId,
            job.AcceptedCountRevision,
            job.Status,
            job.AttemptCount,
            job.Status == StockCountSubmissionJobStatus.RetryScheduled
                ? UtcTime.AsUtc(job.NextAttemptAtUtc)
                : null,
            job.FailureCode,
            UtcTime.AsUtc(job.CreatedAtUtc),
            UtcTime.AsUtc(job.UpdatedAtUtc),
            UtcTime.AsUtc(job.CompletedAtUtc),
            session.Status,
            session.MovementReconciliationStatus);
}
