namespace RetailPOS.Core.Entities;

public static class AccountingFinancialYearCloseRunStatuses
{
    public const string Prepared = "prepared";
    public const string Approved = "approved";
    public const string Executed = "executed";
    public const string Reopened = "reopened";
}

public class AccountingFinancialYearCloseRun
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long FinancialYearId { get; set; }
    public long FinalAccountingPeriodId { get; set; }
    public long FinalPeriodCloseRunId { get; set; }
    public int RunNumber { get; set; }
    public string Status { get; set; } = AccountingFinancialYearCloseRunStatuses.Prepared;
    public long PreparedBy { get; set; }
    public DateTime PreparedAt { get; set; } = DateTime.UtcNow;
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? ExecutedBy { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public long? YearEndJournalEntryId { get; set; }
    public long? ReversalJournalEntryId { get; set; }
    public long? ReopenedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public string? ReopenReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual FinancialYear FinancialYear { get; set; } = null!;
    public virtual AccountingPeriod FinalAccountingPeriod { get; set; } = null!;
    public virtual AccountingPeriodCloseRun FinalPeriodCloseRun { get; set; } = null!;
    public virtual JournalEntry? YearEndJournalEntry { get; set; }
    public virtual JournalEntry? ReversalJournalEntry { get; set; }
}