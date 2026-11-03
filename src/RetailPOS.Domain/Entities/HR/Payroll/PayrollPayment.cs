namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollPayment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long? PayrollPeriodId { get; set; }
    public long? PayrollRunId { get; set; }
    public long? PayrollEmployeeResultId { get; set; }
    public long? FinalSettlementId { get; set; }
    public long? FinalSettlementRevisionId { get; set; }
    public int? FinalSettlementRevisionNo { get; set; }
    public long? ApprovalInstanceId { get; set; }
    public long? HistoricalLocationIdSnapshot { get; set; }
    public int? DecimalPrecisionSnapshot { get; set; }
    public string? RoundingModeSnapshot { get; set; }
    public string? FinalSettlementEvidenceHash { get; set; }
    public long? PaymentBatchId { get; set; }
    public long EmployeeId { get; set; }
    public decimal PayableAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string CurrencyCode { get; set; } = "";
    public string Method { get; set; } = PayrollPaymentMethods.Cash;
    public string Status { get; set; } = PayrollPaymentStatuses.Pending;
    public long Version { get; set; } = 1;
    public string IdempotencyKey { get; set; } = "";
    public string IdempotencyPayloadHash { get; set; } = "";
    public long? OriginalPaymentId { get; set; }
    public int AttemptNo { get; set; } = 1;
    public string? Notes { get; set; }
    public string? ExternalReference { get; set; }
    public string? FailureReturnReason { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public long? HeldByUserId { get; set; }
    public DateTime? HeldAtUtc { get; set; }
    public string? HoldReason { get; set; }
    public long? ReleasedByUserId { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public string? ReleaseReason { get; set; }
    public string ResolutionStatus { get; set; } = PayrollPaymentResolutionStatuses.None;
    public string? ResolutionEvidenceReference { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public PayrollPaymentBatch? PaymentBatch { get; set; }
    public ICollection<PayrollPaymentAllocation> Allocations { get; set; } = [];
    public ICollection<PayrollPaymentEvidence> Evidence { get; set; } = [];
}

public sealed class PayrollPaymentBatch
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollRunId { get; set; }
    public long PayrollPeriodId { get; set; }
    public string BatchReference { get; set; } = "";
    public string Method { get; set; } = PayrollPaymentMethods.Bank;
    public string CurrencyCode { get; set; } = "";
    public decimal ExpectedAmount { get; set; }
    public string Status { get; set; } = PayrollPaymentBatchStatuses.Ready;
    public long Version { get; set; } = 1;
    public string IdempotencyKey { get; set; } = "";
    public string IdempotencyPayloadHash { get; set; } = "";
    public int BankFileVersionNo { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public long? GeneratedByUserId { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public long? SubmittedByUserId { get; set; }
    public string? SubmissionExternalReference { get; set; }
    public string? SubmissionReason { get; set; }
    public string? SubmissionIdempotencyKey { get; set; }
    public string? SubmissionPayloadHash { get; set; }
    public ICollection<PayrollPayment> Payments { get; set; } = [];
    public ICollection<PayrollPaymentBankFileVersion> BankFileVersions { get; set; } = [];
}

public sealed class PayrollPaymentAllocation
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollPaymentId { get; set; }
    public int AllocationNo { get; set; }
    public string Method { get; set; } = PayrollPaymentMethods.Cash;
    public decimal Amount { get; set; }
    public decimal SettledAmount { get; set; }
    public string State { get; set; } = PayrollPaymentAllocationStates.Pending;
    public string? ExternalReference { get; set; }
    public long? PaymentDestinationId { get; set; }
    public int? PaymentDestinationVersionNo { get; set; }
    public string? BankNameSnapshot { get; set; }
    public string? AccountNameSnapshot { get; set; }
    public string? AccountNumberSnapshot { get; set; }
    public string? MobileProviderSnapshot { get; set; }
    public string? MobileAccountNameSnapshot { get; set; }
    public string? MobileAccountNumberSnapshot { get; set; }
}

public static class PayrollPaymentMethods
{
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string Mobile = "Mobile";
    public const string Mixed = "Mixed";

    public static bool IsAllocationMethod(string method) => method is Cash or Bank or Mobile;
}

public static class PayrollPaymentStatuses
{
    public const string Pending = "Pending";
    public const string Ready = "Ready";
    public const string Processing = "Processing";
    public const string PartiallyPaid = "PartiallyPaid";
    public const string Paid = "Paid";
    public const string Held = "Held";
    public const string Failed = "Failed";
    public const string Returned = "Returned";
    public const string Cancelled = "Cancelled";
}

public static class PayrollPaymentAllocationStates
{
    public const string Pending = "Pending";
    public const string PartiallySettled = "PartiallySettled";
    public const string Settled = "Settled";
}

public static class PayrollPaymentEvidenceTypes
{
    public const string Settlement = "Settlement";
    public const string Return = "Return";
}

public static class PayrollPaymentResolutionStatuses
{
    public const string None = "None";
    public const string PendingInvestigation = "PendingInvestigation";
    public const string ResolvedNonSettlement = "ResolvedNonSettlement";
    public const string ResolvedReturn = "ResolvedReturn";
    public const string ResolvedReissue = "ResolvedReissue";
}

public sealed class PayrollPaymentEvidence
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollPaymentId { get; set; }
    public string Type { get; set; } = PayrollPaymentEvidenceTypes.Settlement;
    public decimal Amount { get; set; }
    public DateOnly EvidenceDate { get; set; }
    public string EvidenceReference { get; set; } = "";
    public string AllocationEvidenceJson { get; set; } = "[]";
    public string IdempotencyKey { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public long VerifiedByUserId { get; set; }
    public DateTime VerifiedAtUtc { get; set; }
}

public sealed class PayrollPaymentReconciliation
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollPaymentId { get; set; }
    public long PayrollPaymentAllocationId { get; set; }
    public long? PaymentBatchId { get; set; }
    public long Version { get; set; }
    public long PaymentVersion { get; set; }
    public decimal ExpectedAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal Difference { get; set; }
    public string Status { get; set; } = PayrollPaymentReconciliationStatuses.Unreconciled;
    public DateOnly? SettlementDate { get; set; }
    public string EvidenceSource { get; set; } = "";
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public long ReconciledByUserId { get; set; }
    public DateTime ReconciledAtUtc { get; set; }
}

public static class PayrollPaymentReconciliationStatuses
{
    public const string Unreconciled = "Unreconciled";
    public const string PartiallyReconciled = "PartiallyReconciled";
    public const string Reconciled = "Reconciled";
    public const string Exception = "Exception";
}

public static class PayrollPaymentBatchStatuses
{
    public const string Draft = "Draft";
    public const string Ready = "Ready";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string PartiallyFailed = "PartiallyFailed";
    public const string Failed = "Failed";
}

public sealed class PayrollPaymentBankFileVersion
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PaymentBatchId { get; set; }
    public int VersionNo { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "text/csv";
    public int RowCount { get; set; }
    public decimal ExpectedAmount { get; set; }
    public long GeneratedByUserId { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string GenerationReason { get; set; } = "";
}
