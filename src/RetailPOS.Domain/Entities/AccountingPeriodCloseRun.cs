namespace RetailPOS.Core.Entities;

public static class AccountingPeriodCloseRunStatuses
{
    public const string Prepared = "prepared";
    public const string Approved = "approved";
    public const string Executed = "executed";
    public const string Reopened = "reopened";
    public const string Superseded = "superseded";
}

public static class AccountingCloseGateStatuses
{
    public const string Passed = "passed";
    public const string Warning = "warning";
    public const string Blocking = "blocking";
    public const string ExceptionApproved = "exception_approved";
}

public class AccountingPeriodCloseRun
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long AccountingPeriodId { get; set; }
    public int RunNumber { get; set; }
    public string Status { get; set; } = AccountingPeriodCloseRunStatuses.Prepared;
    public int EvaluationSequence { get; set; }
    public long PreparedBy { get; set; }
    public DateTime PreparedAt { get; set; } = DateTime.UtcNow;
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? ExecutedBy { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public string? ExecutionReason { get; set; }
    public long? ReopenedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public string? ReopenReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual AccountingPeriod AccountingPeriod { get; set; } = null!;
    public virtual ICollection<AccountingCloseGateSnapshot> GateSnapshots { get; set; } = new List<AccountingCloseGateSnapshot>();
    public virtual ICollection<AccountingCloseExceptionApproval> ExceptionApprovals { get; set; } = new List<AccountingCloseExceptionApproval>();
}

public class AccountingCloseGateSnapshot
{
    public long Id { get; set; }
    public long AccountingPeriodCloseRunId { get; set; }
    public int EvaluationSequence { get; set; }
    public string GateCode { get; set; } = string.Empty;
    public string Status { get; set; } = AccountingCloseGateStatuses.Passed;
    public decimal? ExpectedAmount { get; set; }
    public decimal? ActualAmount { get; set; }
    public string? Details { get; set; }
    public bool IsHistoricalCutoverLimitation { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;

    public virtual AccountingPeriodCloseRun CloseRun { get; set; } = null!;
    public virtual ICollection<AccountingCloseExceptionApproval> ExceptionApprovals { get; set; } = new List<AccountingCloseExceptionApproval>();
}

public class AccountingCloseExceptionApproval
{
    public long Id { get; set; }
    public long AccountingPeriodCloseRunId { get; set; }
    public long AccountingCloseGateSnapshotId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long ApprovedBy { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;

    public virtual AccountingPeriodCloseRun CloseRun { get; set; } = null!;
    public virtual AccountingCloseGateSnapshot GateSnapshot { get; set; } = null!;
}