namespace RetailPOS.Core.Entities;

public static class AccountingPeriodStatuses
{
    public const string Open = "Open";
    public const string Closed = "Closed";
    public const string Locked = "Locked";
}

public class AccountingPeriod
{
    public long Id { get; set; }
    public long FinancialYearId { get; set; }
    public int PeriodNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = AccountingPeriodStatuses.Open;
    public DateTime? ClosedAt { get; set; }
    public long? ClosedBy { get; set; }
    public DateTime? LockedAt { get; set; }
    public long? LockedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public long? ReopenedBy { get; set; }
    public string? StatusReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual FinancialYear FinancialYear { get; set; } = null!;
    public virtual ICollection<JournalEntry> JournalEntries { get; set; } = new List<JournalEntry>();
}
