namespace RetailPOS.Core.Entities;

/// <summary>
/// Durable lifecycle of the asynchronous adjustment that follows an ERP StockCount approval.
/// A job is created in the same transaction as the approval and its outbox message. "Completed" means
/// Inventory, StockLedger, Accounting and the StockCount completion were committed together (or that the
/// count had no differences to adjust). "Blocked" means movement evidence was ambiguous and nothing changed.
/// </summary>
public static class StockCountAdjustmentJobStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string RetryScheduled = "RetryScheduled";
    public const string Completed = "Completed";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";

    public static readonly string[] Active = [Pending, Processing, RetryScheduled];

    public static bool IsActive(string status) => Active.Contains(status, StringComparer.Ordinal);
    public static bool IsTerminal(string status) => status is Completed or Blocked or Failed;
}

public sealed class StockCountAdjustmentJob
{
    public Guid Id { get; set; }
    public long StockCountId { get; set; }
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public long? SubmittedByUserId { get; set; }
    public long ApproverUserId { get; set; }
    public string ApproverRoleName { get; set; } = string.Empty;
    public long ApprovedCountRevision { get; set; }
    public string Status { get; set; } = StockCountAdjustmentJobStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public string? EvidenceStatus { get; set; }
    public string? BlockedReasonCodes { get; set; }
    public long? StockAdjustmentId { get; set; }
    public Guid OutboxMessageId { get; set; }
    public Guid AuditCorrelationId { get; set; }
    public string RequestCorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long Version { get; set; }

    public StockCount StockCount { get; set; } = null!;
}
