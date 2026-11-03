namespace RetailPOS.Core.Entities.HR.Payroll;

public sealed class PayrollFinalSettlementAccountingPosting
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long FinalSettlementId { get; set; }
    public long RevisionId { get; set; }
    public int RevisionNo { get; set; }
    public string PostingType { get; set; } = "";
    public long? PaymentId { get; set; }
    public long? PaymentEvidenceId { get; set; }
    public string PostingReference { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string RequestPayloadJson { get; set; } = "";
    public string RequestPayloadHash { get; set; } = "";
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

public static class PayrollFinalSettlementPostingTypes
{
    public const string Expense = "FinalSettlementExpense";
    public const string Receivable = "FinalSettlementReceivable";
    public const string Payment = "FinalSettlementPayment";
}
