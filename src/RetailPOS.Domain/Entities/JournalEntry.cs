namespace RetailPOS.Core.Entities;

public static class JournalEntryStatuses
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Posted = "Posted";
    public const string Reversed = "Reversed";
    public const string Cancelled = "Cancelled";
}

public class JournalEntry
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string? JournalNumber { get; set; }
    public string JournalType { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public DateTime PostingDate { get; set; }
    public DateTime? BusinessDate { get; set; }
    public long? FinancialYearId { get; set; }
    public long? AccountingPeriodId { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? TerminalId { get; set; }
    public long? CashSessionId { get; set; }
    public string Status { get; set; } = JournalEntryStatuses.Draft;
    public bool IsSystemGenerated { get; set; }
    public long? ReversalOfJournalEntryId { get; set; }
    public long? ReversedByJournalEntryId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public long? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public long? RejectedBy { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? PostedAt { get; set; }
    public long? PostedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public long? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? ReversedAt { get; set; }
    public long? ReversedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual FinancialYear? FinancialYear { get; set; }
    public virtual AccountingPeriod? AccountingPeriod { get; set; }
    public virtual Outlet? Outlet { get; set; }
    public virtual Warehouse? Warehouse { get; set; }
    public virtual PosTerminal? Terminal { get; set; }
    public virtual CashSession? CashSession { get; set; }
    public virtual JournalEntry? ReversalOfJournalEntry { get; set; }
    public virtual JournalEntry? ReversedByJournalEntry { get; set; }
    public virtual ICollection<JournalEntry> ReversalEntries { get; set; } = new List<JournalEntry>();
    public virtual ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
    public virtual ICollection<JournalEntryAttachment> Attachments { get; set; } = new List<JournalEntryAttachment>();
}
