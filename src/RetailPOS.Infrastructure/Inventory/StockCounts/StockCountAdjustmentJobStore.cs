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

public static class StockCountAdjustmentMessage
{
    public const string Producer = "RetailPOS";
    public const string MessageType = "stock-count.adjustment.requested";
    public const string AggregateType = "StockCountAdjustmentJob";
    public const int SchemaVersion = 1;

    public sealed record Payload(long BusinessId, Guid JobId, long StockCountId, long ApprovedCountRevision);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(Payload payload) => JsonSerializer.Serialize(payload, JsonOptions);

    public static Payload? Deserialize(string json) => JsonSerializer.Deserialize<Payload>(json, JsonOptions);
}

public sealed class StockCountAdjustmentJobStore(
    RetailPOSDbContext dbContext,
    IRequiredAuditTransaction auditTransaction,
    IIntegrationOutbox outbox,
    IAuditContext audit,
    TimeProvider clock) : IStockCountAdjustmentJobStore
{
    public Task<ApproveStockCountWithAdjustmentResult> ApproveAndEnqueueAsync(
        ApproveStockCountWithAdjustmentInput input,
        CancellationToken cancellationToken) =>
        auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(
                input.BusinessId,
                "approve",
                "inventory",
                "Approve StockCount and enqueue the asynchronous stock adjustment",
                nameof(StockCount),
                input.StockCountId.ToString()),
            token => ApproveCoreAsync(input, token),
            result => !result.IsReplay,
            cancellationToken);

    private async Task<ApproveStockCountWithAdjustmentResult> ApproveCoreAsync(
        ApproveStockCountWithAdjustmentInput input,
        CancellationToken cancellationToken)
    {
        var stockCount = await dbContext.StockCounts
            .FromSqlInterpolated($"SELECT * FROM stock_counts WHERE id = {input.StockCountId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Stock count with ID {input.StockCountId} not found");
        if (stockCount.BusinessId != input.BusinessId
            || stockCount.LocationId != input.LocationId
            || stockCount.LocationType != input.LocationType)
        {
            throw new UnauthorizedAccessException("You are not authorized to access this stock count.");
        }

        // A repeated click or client retry after the decision committed returns the same durable job.
        var existing = await dbContext.StockCountAdjustmentJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                job => job.StockCountId == stockCount.Id
                    && job.ApprovedCountRevision == stockCount.CountRevision,
                cancellationToken);
        if (existing is not null && stockCount.Status != StockCount.StatusSubmitted)
            return new ApproveStockCountWithAdjustmentResult(ToReadModel(existing), IsReplay: true);

        if (stockCount.Status != StockCount.StatusSubmitted)
            throw new InvalidOperationException("Only Submitted stock counts can be approved.");

        await dbContext.Entry(stockCount)
            .Collection(candidate => candidate.Lines)
            .LoadAsync(cancellationToken);
        if (stockCount.Lines.Any(line => !line.PhysicalStock.HasValue))
            throw new InvalidOperationException("All lines must have physical counts before approval.");

        if (stockCount.SubmittedBy == input.ApproverUserId)
        {
            throw new StockCountApprovalPolicyException(
                "Stock count approval requires an approver different from the user who submitted it.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        stockCount.Status = StockCount.StatusApproved;
        stockCount.ApprovedBy = input.ApproverUserId;
        stockCount.ApprovedAt = now;

        var job = new StockCountAdjustmentJob
        {
            Id = Guid.NewGuid(),
            StockCountId = stockCount.Id,
            BusinessId = stockCount.BusinessId,
            LocationId = stockCount.LocationId,
            LocationType = stockCount.LocationType,
            SubmittedByUserId = stockCount.SubmittedBy,
            ApproverUserId = input.ApproverUserId,
            ApproverRoleName = input.ApproverRoleName,
            ApprovedCountRevision = stockCount.CountRevision,
            Status = StockCountAdjustmentJobStatus.Pending,
            NextAttemptAtUtc = now,
            OutboxMessageId = Guid.NewGuid(),
            AuditCorrelationId = audit.CorrelationId,
            RequestCorrelationId = input.RequestCorrelationId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1
        };
        dbContext.StockCountAdjustmentJobs.Add(job);

        var payload = StockCountAdjustmentMessage.Serialize(new StockCountAdjustmentMessage.Payload(
            job.BusinessId,
            job.Id,
            job.StockCountId,
            job.ApprovedCountRevision));
        await outbox.AppendAsync(
            new IntegrationEnvelope(
                job.OutboxMessageId,
                job.BusinessId,
                StockCountAdjustmentMessage.Producer,
                StockCountAdjustmentMessage.MessageType,
                StockCountAdjustmentMessage.SchemaVersion,
                StockCountAdjustmentMessage.AggregateType,
                job.Id.ToString(),
                audit.CorrelationId,
                null,
                new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc),
                payload,
                IntegrationEnvelope.Hash(payload)),
            cancellationToken);

        return new ApproveStockCountWithAdjustmentResult(ToReadModel(job), IsReplay: false);
    }

    internal static StockCountAdjustmentJobReadModel ToReadModel(StockCountAdjustmentJob job) =>
        new(
            job.Id,
            job.StockCountId,
            job.ApprovedCountRevision,
            job.Status,
            job.AttemptCount,
            job.Status == StockCountAdjustmentJobStatus.RetryScheduled
                ? UtcTime.AsUtc(job.NextAttemptAtUtc)
                : null,
            job.FailureCode,
            job.EvidenceStatus,
            string.IsNullOrWhiteSpace(job.BlockedReasonCodes)
                ? []
                : job.BlockedReasonCodes.Split(',', StringSplitOptions.RemoveEmptyEntries),
            job.StockAdjustmentId,
            UtcTime.AsUtc(job.CreatedAtUtc),
            UtcTime.AsUtc(job.UpdatedAtUtc),
            UtcTime.AsUtc(job.CompletedAtUtc));
}
