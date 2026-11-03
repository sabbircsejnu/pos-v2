namespace RetailPOS.Core.Entities;

public class BankReconciliationMatch
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long BankAccountId { get; set; }
    public long BankReconciliationSessionId { get; set; }
    public string Status { get; set; } = BankReconciliationMatchStatuses.Effective;
    public decimal StatementTotal { get; set; }
    public decimal GlTotal { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UnmatchedAt { get; set; }
    public long? UnmatchedBy { get; set; }
    public string? UnmatchReason { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual BankAccount BankAccount { get; set; } = null!;
    public virtual BankReconciliationSession Session { get; set; } = null!;
    public virtual ICollection<BankReconciliationMatchLine> Lines { get; set; } = new List<BankReconciliationMatchLine>();
}

public class BankReconciliationMatchLine
{
    public long Id { get; set; }
    public long BankReconciliationMatchId { get; set; }
    public long? BankStatementLineId { get; set; }
    public long? JournalEntryLineId { get; set; }
    public decimal SignedAmount { get; set; }
    public bool IsActive { get; set; } = true;
    public virtual BankReconciliationMatch Match { get; set; } = null!;
    public virtual BankStatementLine? StatementLine { get; set; }
    public virtual JournalEntryLine? JournalEntryLine { get; set; }
}

public static class BankReconciliationMatchStatuses
{
    public const string Effective = "effective";
    public const string Unmatched = "unmatched";
}
