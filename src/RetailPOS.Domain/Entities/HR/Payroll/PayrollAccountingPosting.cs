namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollAccountingPosting
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PayrollPeriodId { get; set; }
    public long PayrollRunId { get; set; }
    public int RunRevision { get; set; }
    public string PostingType { get; set; } = PayrollAccountingPostingTypes.PayrollExpense;
    public long? PaymentBatchId { get; set; }
    public long? PayrollPaymentId { get; set; }
    public long? PayrollPaymentAllocationId { get; set; }
    public long? PayrollPaymentEvidenceId { get; set; }
    public long? PayrollPaymentReconciliationId { get; set; }
    public string? CorrectionReference { get; set; }
    public long? OriginalPostingId { get; set; }
    public long? ReversalJournalId { get; set; }
    public string? CorrectionPolicyVersion { get; set; }
    public string PostingReference { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string? RequestPayloadJson { get; set; }
    public string? RequestPayloadHash { get; set; }
    public string? ConflictPayloadHash { get; set; }
    public long? AccountingJournalId { get; set; }
    public string? AccountingReference { get; set; }
    public string Status { get; set; } = PayrollAccountingPostingStatuses.Pending;
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
    public long Version { get; set; } = 1;
    public long RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? PostedAtUtc { get; set; }
}

public sealed class PayrollAccountingComponentMapping
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string ComponentType { get; set; } = "";
    public string ComponentCode { get; set; } = "";
    public long AccountId { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public long UpdatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public static class PayrollAccountingPostingTypes
{
    public const string PayrollExpense = "PayrollExpense";
    public const string SalaryPayment = "SalaryPayment";
    public const string PayrollCorrection = "PayrollCorrection";
}

public static class PayrollAccountingPostingStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Posted = "Posted";
    public const string Failed = "Failed";
    public const string Conflict = "Conflict";
}

public static class PayrollAccountingComponentTypes
{
    public const string Earning = "Earning";
    public const string Deduction = "Deduction";
    public const string NetPayable = "NetPayable";
    public const string FinalSettlementReceivable = "FinalSettlementReceivable";
}
