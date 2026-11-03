using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollEmployeeLoan
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long EmployeeId { get; set; }
    public long OperationalLocationProjectionId { get; set; }
    public string LoanType { get; set; } = "";
    public decimal PrincipalAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public DateOnly? DisbursedDate { get; set; }
    public bool IsOpeningBalance { get; set; }
    public DateOnly? OpeningDisbursedDate { get; set; }
    public decimal? OpeningBalanceAmount { get; set; }
    public DateOnly RecoveryStartDate { get; set; }
    public string RecoveryMode { get; set; } = "";
    public decimal? FixedInstallment { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string ProposedScheduleJson { get; set; } = "[]";
    public string RequestHash { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string IdempotencyPayloadHash { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? Reference { get; set; }
    public string Status { get; set; } = "Draft";
    public long? ApprovalInstanceId { get; set; }
    public ApprovalInstance? ApprovalInstance { get; set; }
    public int? CurrentScheduleVersionNo { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public long? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public long? SubmittedTargetVersion { get; set; }
    public long? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class PayrollLoanScheduleVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LoanId { get; set; }
    public int VersionNo { get; set; }
    public DateOnly RecoveryStartDate { get; set; }
    public string RecoveryMode { get; set; } = "";
    public decimal? FixedInstallment { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string IdempotencyPayloadHash { get; set; } = "";
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<PayrollLoanScheduleLine> Lines { get; set; } = [];
}

public sealed class PayrollLoanScheduleLine
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LoanId { get; set; }
    public long ScheduleVersionId { get; set; }
    public int InstallmentNo { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal PlannedAmount { get; set; }
}

public sealed class PayrollLoanRepayment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LoanId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }
    public string? Reference { get; set; }
    public string Reason { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string IdempotencyPayloadHash { get; set; } = "";
    public long RecordedByUserId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public List<PayrollLoanRepaymentAllocation> Allocations { get; set; } = [];
}

public sealed class PayrollLoanRepaymentAllocation
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LoanId { get; set; }
    public long RepaymentId { get; set; }
    public long ScheduleVersionId { get; set; }
    public int InstallmentNo { get; set; }
    public decimal Amount { get; set; }
}
