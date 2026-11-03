namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollPeriod
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string PeriodCode { get; set; } = "";
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateOnly? PayDate { get; set; }
    public string Status { get; set; } = PayrollPeriodStatuses.Open;
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public long OpenedByUserId { get; set; }
    public DateTime? ProcessingAtUtc { get; set; }
    public long? ProcessingByUserId { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public long? LockedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public long? PostedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public long? ClosedByUserId { get; set; }
    public string? ClosedReason { get; set; }
    public DateTime? ReopenedAtUtc { get; set; }
    public long? ReopenedByUserId { get; set; }
    public string? ReopenReason { get; set; }
}

public sealed class PayrollPeriodCloseAssessment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollPeriodId { get; set; }
    public long PeriodVersion { get; set; }
    public string StatusSnapshot { get; set; } = "";
    public int PolicyVersion { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string GateResultsJson { get; set; } = "[]";
    public string RunRevisionRefsJson { get; set; } = "[]";
    public string PaymentStateVersionsJson { get; set; } = "[]";
    public string PostingStateVersionsJson { get; set; } = "[]";
    public string ExceptionsJson { get; set; } = "[]";
    public string? Reason { get; set; }
    public long ActorUserId { get; set; }
    public DateTime AssessedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public bool WasClosed { get; set; }
    public long ResultPeriodVersion { get; set; }
}

public sealed class PayrollPeriodReopenEvent
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollPeriodId { get; set; }
    public string PreviousStatus { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Purpose { get; set; } = "";
    public long ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public long PreviousPeriodVersion { get; set; }
    public long ResultPeriodVersion { get; set; }
    public long? OriginalPayrollRunId { get; set; }
    public long? CorrectionPayrollRunId { get; set; }
}

public static class PayrollPeriodStatuses
{
    public const string Open = "Open";
    public const string Processing = "Processing";
    public const string Locked = "Locked";
    public const string Approved = "Approved";
    public const string Posted = "Posted";
    public const string Closed = "Closed";
}
