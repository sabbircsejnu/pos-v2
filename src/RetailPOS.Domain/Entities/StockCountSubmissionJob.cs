namespace RetailPOS.Core.Entities;

/// <summary>
/// Durable lifecycle of a Mobile Stock Take submission. "ErpSubmitted" means only that the
/// existing ERP StockCount submission workflow succeeded; it is not approval, reconciliation or posting.
/// </summary>
public static class StockCountSubmissionJobStatus
{
    public const string Accepted = "Accepted";
    public const string Processing = "Processing";
    public const string RetryScheduled = "RetryScheduled";
    public const string ErpSubmitted = "ErpSubmitted";
    public const string Failed = "Failed";

    public static readonly string[] Active = [Accepted, Processing, RetryScheduled];

    public static bool IsActive(string status) => Active.Contains(status, StringComparer.Ordinal);
    public static bool IsTerminal(string status) => status is ErpSubmitted or Failed;
}

public sealed class StockCountSubmissionJob
{
    public Guid Id { get; set; }
    public long StockCountId { get; set; }
    public long BusinessId { get; set; }
    public long OutletId { get; set; }
    public long ActorUserId { get; set; }
    public string ActorRoleName { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public long AcceptedCountRevision { get; set; }
    public string Status { get; set; } = StockCountSubmissionJobStatus.Accepted;
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public Guid OutboxMessageId { get; set; }
    public Guid AuditCorrelationId { get; set; }
    public string RequestCorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long Version { get; set; }

    public StockCount StockCount { get; set; } = null!;
}
