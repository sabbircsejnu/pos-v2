namespace RetailPOS.Core.Entities;

public class FinancialYear
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string YearCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ClosedAt { get; set; }
    public long? ClosedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual ICollection<AccountingNumberSequence> NumberSequences { get; set; } = new List<AccountingNumberSequence>();
    public virtual ICollection<AccountingPeriod> Periods { get; set; } = new List<AccountingPeriod>();
    public virtual ICollection<JournalEntry> JournalEntries { get; set; } = new List<JournalEntry>();
}
