namespace RetailPOS.Core.Entities;

public class BankStatementAdjustment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long BankAccountId { get; set; }
    public long BankStatementLineId { get; set; }
    public string AdjustmentType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = BankStatementAdjustmentStatuses.Proposed;
    public string? Reason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public long? JournalEntryId { get; set; }
    public long? ReversalJournalEntryId { get; set; }
    public long ProposedBy { get; set; }
    public DateTime ProposedAt { get; set; } = DateTime.UtcNow;
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ReversedAt { get; set; }
    public long? ReversedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual BankAccount BankAccount { get; set; } = null!;
    public virtual BankStatementLine StatementLine { get; set; } = null!;
    public virtual JournalEntry? JournalEntry { get; set; }
    public virtual JournalEntry? ReversalJournalEntry { get; set; }
}

public static class BankStatementAdjustmentTypes { public const string BankFee = "bank_fee"; public const string BankInterest = "bank_interest"; }
public static class BankStatementAdjustmentStatuses { public const string Proposed = "proposed"; public const string Approved = "approved"; public const string Reversed = "reversed"; }
