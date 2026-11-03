using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockAdjustmentAuditWriter(
    IAuditService auditService,
    ILogger<StockAdjustmentAuditWriter> logger)
    : IStockAdjustmentAuditWriter
{
    public async Task RecordCreatedAsync(
        StockAdjustmentReadModel adjustment,
        bool submittedForApproval,
        CancellationToken cancellationToken)
    {
        await TryRecordAsync(
            BuildLifecycleAudit(
                adjustment,
                AuditActionType.Create,
                AuditOperationType.Insert,
                $"Stock adjustment {adjustment.AdjustmentNumber} created as {adjustment.Status}",
                adjustment.AdjustedBy,
                []),
            "create stock adjustment",
            cancellationToken);

        if (submittedForApproval)
        {
            await TryRecordAsync(
                BuildLifecycleAudit(
                    adjustment,
                    AuditActionType.Submit,
                    AuditOperationType.Update,
                    $"Stock adjustment {adjustment.AdjustmentNumber} submitted for approval",
                    adjustment.AdjustedBy,
                    [
                        ("Status", StockAdjustment.StatusDraft, StockAdjustment.StatusPendingApproval),
                        ("SubmittedAt", null, adjustment.SubmittedAt)
                    ]),
                "submit stock adjustment",
                cancellationToken);
        }
    }

    public Task RecordUpdatedAsync(
        StockAdjustmentReadModel before,
        StockAdjustmentReadModel after,
        long updatedBy,
        CancellationToken cancellationToken) =>
        TryRecordAsync(
            BuildLifecycleAudit(
                after,
                AuditActionType.Update,
                AuditOperationType.Update,
                $"Stock adjustment {after.AdjustmentNumber} edited",
                updatedBy,
                [
                    ("LocationId", before.LocationId, after.LocationId),
                    ("LocationType", before.LocationType, after.LocationType),
                    ("LineCount", before.LineCount, after.LineCount),
                    ("NetQuantityChange", before.NetQuantityChange, after.NetQuantityChange),
                    ("UpdatedAt", before.UpdatedAt, after.UpdatedAt)
                ]),
            "update stock adjustment",
            cancellationToken);

    public Task RecordCancelledAsync(
        StockAdjustmentReadModel adjustment,
        string previousStatus,
        long cancelledBy,
        CancellationToken cancellationToken) =>
        TryRecordAsync(
            BuildLifecycleAudit(
                adjustment,
                AuditActionType.Cancel,
                AuditOperationType.Update,
                $"Stock adjustment {adjustment.AdjustmentNumber} cancelled",
                cancelledBy,
                [
                    ("Status", previousStatus, StockAdjustment.StatusCancelled),
                    ("CancelledAt", null, adjustment.CancelledAt)
                ]),
            "cancel stock adjustment",
            cancellationToken);

    public Task RecordReversedAsync(
        StockAdjustmentReadModel adjustment,
        string reason,
        long reversedBy,
        long? originalJournalEntryId,
        long? reversalJournalEntryId,
        CancellationToken cancellationToken) =>
        TryRecordAsync(
            BuildLifecycleAudit(
                adjustment,
                AuditActionType.Reverse,
                AuditOperationType.Update,
                $"Stock adjustment {adjustment.AdjustmentNumber} reversed",
                reversedBy,
                [
                    ("Status", StockAdjustment.StatusApproved, StockAdjustment.StatusReversed),
                    ("ReversalReason", null, reason),
                    ("JournalEntryId", originalJournalEntryId, originalJournalEntryId),
                    ("ReversalJournalEntryId", null, reversalJournalEntryId)
                ],
                journalEntryId: originalJournalEntryId,
                reversalJournalEntryId: reversalJournalEntryId),
            "reverse stock adjustment",
            cancellationToken);

    public async Task RecordApprovedAsync(
        StockAdjustmentReadModel adjustment,
        long approvedBy,
        long? journalEntryId,
        CancellationToken cancellationToken)
    {
        var fields = new (string FieldName, object? OldValue, object? NewValue)[]
        {
            ("Status", StockAdjustment.StatusPendingApproval, StockAdjustment.StatusApproved),
            ("ApprovedAt", null, adjustment.ApprovedAt),
            ("JournalEntryId", null, journalEntryId)
        };
        var entities = new List<AuditEntityInput>
        {
            new(
                "StockAdjustment",
                adjustment.Id.ToString(),
                AuditOperationType.Update,
                fields,
                metadata: $"{{\"adjustmentNumber\":\"{EscapeJson(adjustment.AdjustmentNumber)}\",\"status\":\"{EscapeJson(adjustment.Status)}\",\"locationId\":{adjustment.LocationId},\"locationType\":\"{EscapeJson(adjustment.LocationType)}\",\"locationName\":\"{EscapeJson(adjustment.LocationName)}\",\"lineCount\":{adjustment.LineCount},\"netQuantityChange\":{adjustment.NetQuantityChange},\"actorUserId\":{approvedBy}}}")
        };

        foreach (var line in adjustment.Lines)
        {
            entities.Add(new AuditEntityInput(
                "StockAdjustmentLine",
                line.Id.ToString(),
                AuditOperationType.Update,
                [
                    ("VariantId", null, line.VariantId),
                    ("PreviousQuantity", null, line.PreviousQuantity),
                    ("AdjustmentQuantity", null, line.QuantityChange),
                    ("NewQuantity", null, line.NewQuantity),
                    ("Reason", null, line.Reason),
                    ("Notes", null, line.Notes)
                ],
                metadata: $"{{\"variantSku\":\"{EscapeJson(line.VariantSku)}\",\"productName\":\"{EscapeJson(line.ProductName)}\",\"productCode\":\"{EscapeJson(line.ProductCode)}\"}}"));
            entities.Add(new AuditEntityInput(
                "Inventory",
                $"{adjustment.LocationType}:{adjustment.LocationId}:variant:{line.VariantId}",
                AuditOperationType.Update,
                [("Quantity", line.PreviousQuantity, line.NewQuantity)],
                metadata: $"{{\"locationId\":{adjustment.LocationId},\"locationType\":\"{EscapeJson(adjustment.LocationType)}\",\"variantId\":{line.VariantId},\"adjustmentLineId\":{line.Id}}}"));
        }

        await TryRecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Approve,
                Module = AuditModule.Inventory,
                Summary = $"Stock adjustment {adjustment.AdjustmentNumber} approved and inventory updated",
                PrimaryEntity = ("StockAdjustment", adjustment.Id.ToString()),
                Entities = entities,
                Metadata = new Dictionary<string, object?>
                {
                    ["adjustmentNumber"] = adjustment.AdjustmentNumber,
                    ["status"] = adjustment.Status,
                    ["userId"] = adjustment.AdjustedBy,
                    ["userName"] = adjustment.AdjusterName,
                    ["userRole"] = string.IsNullOrWhiteSpace(adjustment.ApprovedByName)
                        ? adjustment.AdjusterName
                        : adjustment.ApprovedByName,
                    ["locationId"] = adjustment.LocationId,
                    ["locationType"] = adjustment.LocationType,
                    ["sourceStockCountId"] = adjustment.SourceStockCountId,
                    ["locationName"] = adjustment.LocationName,
                    ["lineCount"] = adjustment.LineCount,
                    ["totalIncrease"] = adjustment.TotalIncrease,
                    ["totalDecrease"] = adjustment.TotalDecrease,
                    ["netQuantityChange"] = adjustment.NetQuantityChange,
                    ["lines"] = adjustment.Lines.Select(line => new Dictionary<string, object?>
                    {
                        ["variantId"] = line.VariantId,
                        ["variantSku"] = line.VariantSku,
                        ["productName"] = line.ProductName,
                        ["productCode"] = line.ProductCode,
                        ["previousQuantity"] = line.PreviousQuantity,
                        ["adjustmentQuantity"] = line.QuantityChange,
                        ["newQuantity"] = line.NewQuantity,
                        ["reason"] = line.Reason,
                        ["notes"] = line.Notes
                    }).ToList(),
                    ["timestamp"] = adjustment.UpdatedAt == default
                        ? adjustment.AdjustmentDate
                        : adjustment.UpdatedAt,
                    ["approvedBy"] = adjustment.ApprovedBy,
                    ["approvedByName"] = adjustment.ApprovedByName,
                    ["approvedAt"] = adjustment.ApprovedAt,
                    ["rejectedBy"] = adjustment.RejectedBy,
                    ["rejectedByName"] = adjustment.RejectedByName,
                    ["rejectedAt"] = adjustment.RejectedAt,
                    ["rejectionReason"] = adjustment.RejectionReason,
                    ["cancelledBy"] = adjustment.CancelledBy,
                    ["cancelledByName"] = adjustment.CancelledByName,
                    ["cancelledAt"] = adjustment.CancelledAt,
                    ["journalEntryId"] = journalEntryId,
                    ["reversalJournalEntryId"] = null
                }
            },
            "approve stock adjustment",
            cancellationToken);
    }

    public Task RecordSubmittedAsync(
        StockAdjustmentReadModel adjustment,
        long submittedBy,
        CancellationToken cancellationToken) =>
        TryRecordAsync(
            BuildStatusAudit(
                adjustment,
                AuditActionType.Submit,
                $"Stock adjustment {adjustment.AdjustmentNumber} submitted for approval",
                submittedBy,
                [
                    ("Status", StockAdjustment.StatusDraft, StockAdjustment.StatusPendingApproval),
                    ("SubmittedAt", null, adjustment.SubmittedAt)
                ]),
            "submit stock adjustment",
            cancellationToken);

    public Task RecordRejectedAsync(
        StockAdjustmentReadModel adjustment,
        long rejectedBy,
        CancellationToken cancellationToken) =>
        TryRecordAsync(
            BuildStatusAudit(
                adjustment,
                AuditActionType.Reject,
                $"Stock adjustment {adjustment.AdjustmentNumber} rejected",
                rejectedBy,
                [
                    ("Status", StockAdjustment.StatusPendingApproval, StockAdjustment.StatusRejected),
                    ("RejectedAt", null, adjustment.RejectedAt),
                    ("RejectionReason", null, adjustment.RejectionReason)
                ]),
            "reject stock adjustment",
            cancellationToken);

    private static AuditEventInput BuildStatusAudit(
        StockAdjustmentReadModel adjustment,
        string actionType,
        string summary,
        long actorUserId,
        IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)> fields)
    {
        var entities = new List<AuditEntityInput>
        {
            new(
                "StockAdjustment",
                adjustment.Id.ToString(),
                AuditOperationType.Update,
                fields,
                metadata: $"{{\"adjustmentNumber\":\"{EscapeJson(adjustment.AdjustmentNumber)}\",\"status\":\"{EscapeJson(adjustment.Status)}\",\"locationId\":{adjustment.LocationId},\"locationType\":\"{EscapeJson(adjustment.LocationType)}\",\"locationName\":\"{EscapeJson(adjustment.LocationName)}\",\"lineCount\":{adjustment.LineCount},\"netQuantityChange\":{adjustment.NetQuantityChange},\"actorUserId\":{actorUserId}}}")
        };
        foreach (var line in adjustment.Lines)
        {
            entities.Add(new AuditEntityInput(
                "StockAdjustmentLine",
                line.Id.ToString(),
                AuditOperationType.Update,
                [
                    ("VariantId", null, line.VariantId),
                    ("PreviousQuantity", null, line.PreviousQuantity),
                    ("AdjustmentQuantity", null, line.QuantityChange),
                    ("NewQuantity", null, line.NewQuantity),
                    ("Reason", null, line.Reason),
                    ("Notes", null, line.Notes)
                ],
                metadata: $"{{\"variantSku\":\"{EscapeJson(line.VariantSku)}\",\"productName\":\"{EscapeJson(line.ProductName)}\",\"productCode\":\"{EscapeJson(line.ProductCode)}\"}}"));
        }

        var actorName = adjustment.ApprovedBy == actorUserId
            && !string.IsNullOrWhiteSpace(adjustment.ApprovedByName)
                ? adjustment.ApprovedByName
                : adjustment.RejectedBy == actorUserId
                    && !string.IsNullOrWhiteSpace(adjustment.RejectedByName)
                        ? adjustment.RejectedByName
                        : adjustment.CancelledBy == actorUserId
                            && !string.IsNullOrWhiteSpace(adjustment.CancelledByName)
                                ? adjustment.CancelledByName
                                : adjustment.AdjusterName;
        return new AuditEventInput
        {
            ActionType = actionType,
            Module = AuditModule.Inventory,
            Summary = summary,
            PrimaryEntity = ("StockAdjustment", adjustment.Id.ToString()),
            Entities = entities,
            Metadata = new Dictionary<string, object?>
            {
                ["adjustmentNumber"] = adjustment.AdjustmentNumber,
                ["status"] = adjustment.Status,
                ["userId"] = adjustment.AdjustedBy,
                ["userName"] = adjustment.AdjusterName,
                ["userRole"] = actorName,
                ["locationId"] = adjustment.LocationId,
                ["locationType"] = adjustment.LocationType,
                ["sourceStockCountId"] = adjustment.SourceStockCountId,
                ["locationName"] = adjustment.LocationName,
                ["lineCount"] = adjustment.LineCount,
                ["totalIncrease"] = adjustment.TotalIncrease,
                ["totalDecrease"] = adjustment.TotalDecrease,
                ["netQuantityChange"] = adjustment.NetQuantityChange,
                ["lines"] = adjustment.Lines.Select(line => new Dictionary<string, object?>
                {
                    ["variantId"] = line.VariantId,
                    ["variantSku"] = line.VariantSku,
                    ["productName"] = line.ProductName,
                    ["productCode"] = line.ProductCode,
                    ["previousQuantity"] = line.PreviousQuantity,
                    ["adjustmentQuantity"] = line.QuantityChange,
                    ["newQuantity"] = line.NewQuantity,
                    ["reason"] = line.Reason,
                    ["notes"] = line.Notes
                }).ToList(),
                ["timestamp"] = adjustment.UpdatedAt == default
                    ? adjustment.AdjustmentDate
                    : adjustment.UpdatedAt,
                ["approvedBy"] = adjustment.ApprovedBy,
                ["approvedByName"] = adjustment.ApprovedByName,
                ["approvedAt"] = adjustment.ApprovedAt,
                ["rejectedBy"] = adjustment.RejectedBy,
                ["rejectedByName"] = adjustment.RejectedByName,
                ["rejectedAt"] = adjustment.RejectedAt,
                ["rejectionReason"] = adjustment.RejectionReason,
                ["cancelledBy"] = adjustment.CancelledBy,
                ["cancelledByName"] = adjustment.CancelledByName,
                ["cancelledAt"] = adjustment.CancelledAt,
                ["journalEntryId"] = null,
                ["reversalJournalEntryId"] = null
            }
        };
    }

    private async Task TryRecordAsync(
        AuditEventInput input,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditService.RecordAsync(input, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to audit {Operation}", operation);
        }
    }

    private static AuditEventInput BuildLifecycleAudit(
        StockAdjustmentReadModel adjustment,
        string actionType,
        string operationType,
        string summary,
        long actorUserId,
        IReadOnlyList<(string FieldName, object? OldValue, object? NewValue)> fields,
        long? journalEntryId = null,
        long? reversalJournalEntryId = null)
    {
        var entities = new List<AuditEntityInput>
        {
            new(
                "StockAdjustment",
                adjustment.Id.ToString(),
                operationType,
                fields,
                metadata: $"{{\"adjustmentNumber\":\"{EscapeJson(adjustment.AdjustmentNumber)}\",\"status\":\"{EscapeJson(adjustment.Status)}\",\"locationId\":{adjustment.LocationId},\"locationType\":\"{EscapeJson(adjustment.LocationType)}\",\"locationName\":\"{EscapeJson(adjustment.LocationName)}\",\"lineCount\":{adjustment.LineCount},\"netQuantityChange\":{adjustment.NetQuantityChange},\"actorUserId\":{actorUserId}}}")
        };

        foreach (var line in adjustment.Lines)
        {
            entities.Add(new AuditEntityInput(
                "StockAdjustmentLine",
                line.Id.ToString(),
                operationType,
                [
                    ("VariantId", null, line.VariantId),
                    ("PreviousQuantity", null, line.PreviousQuantity),
                    ("AdjustmentQuantity", null, line.QuantityChange),
                    ("NewQuantity", null, line.NewQuantity),
                    ("Reason", null, line.Reason),
                    ("Notes", null, line.Notes)
                ],
                metadata: $"{{\"variantSku\":\"{EscapeJson(line.VariantSku)}\",\"productName\":\"{EscapeJson(line.ProductName)}\",\"productCode\":\"{EscapeJson(line.ProductCode)}\"}}"));
        }

        return new AuditEventInput
        {
            ActionType = actionType,
            Module = AuditModule.Inventory,
            Summary = summary,
            PrimaryEntity = ("StockAdjustment", adjustment.Id.ToString()),
            Entities = entities,
            Metadata = new Dictionary<string, object?>
            {
                ["adjustmentNumber"] = adjustment.AdjustmentNumber,
                ["status"] = adjustment.Status,
                ["userId"] = adjustment.AdjustedBy,
                ["userName"] = adjustment.AdjusterName,
                ["userRole"] = adjustment.AdjusterName,
                ["locationId"] = adjustment.LocationId,
                ["locationType"] = adjustment.LocationType,
                ["sourceStockCountId"] = adjustment.SourceStockCountId,
                ["locationName"] = adjustment.LocationName,
                ["lineCount"] = adjustment.LineCount,
                ["totalIncrease"] = adjustment.TotalIncrease,
                ["totalDecrease"] = adjustment.TotalDecrease,
                ["netQuantityChange"] = adjustment.NetQuantityChange,
                ["lines"] = adjustment.Lines.Select(line => new Dictionary<string, object?>
                {
                    ["variantId"] = line.VariantId,
                    ["variantSku"] = line.VariantSku,
                    ["productName"] = line.ProductName,
                    ["productCode"] = line.ProductCode,
                    ["previousQuantity"] = line.PreviousQuantity,
                    ["adjustmentQuantity"] = line.QuantityChange,
                    ["newQuantity"] = line.NewQuantity,
                    ["reason"] = line.Reason,
                    ["notes"] = line.Notes
                }).ToList(),
                ["timestamp"] = adjustment.UpdatedAt == default
                    ? adjustment.AdjustmentDate
                    : adjustment.UpdatedAt,
                ["approvedBy"] = adjustment.ApprovedBy,
                ["approvedByName"] = adjustment.ApprovedByName,
                ["approvedAt"] = adjustment.ApprovedAt,
                ["rejectedBy"] = adjustment.RejectedBy,
                ["rejectedByName"] = adjustment.RejectedByName,
                ["rejectedAt"] = adjustment.RejectedAt,
                ["rejectionReason"] = adjustment.RejectionReason,
                ["cancelledBy"] = adjustment.CancelledBy,
                ["cancelledByName"] = adjustment.CancelledByName,
                ["cancelledAt"] = adjustment.CancelledAt,
                ["journalEntryId"] = journalEntryId,
                ["reversalJournalEntryId"] = reversalJournalEntryId
            }
        };
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
