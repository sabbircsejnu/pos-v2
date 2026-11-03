namespace RetailPOS.Core.Entities;

public class SettlementBatch
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string SettlementType { get; set; } = string.Empty;
    public long ClearingAccountId { get; set; }
    public long BankAccountId { get; set; }
    public DateTime SettlementDate { get; set; }
    public decimal GrossClearingAmount { get; set; }
    public decimal SettlementFeeAmount { get; set; }
    public decimal NetBankAmount { get; set; }
    public string? ExternalReference { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = SettlementBatchStatuses.Draft;
    public long? JournalEntryId { get; set; }
    public long? ReversalJournalEntryId { get; set; }
    public DateTime? ReversedAt { get; set; }
    public long? ReversedBy { get; set; }
    public string? ReversalReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual ChartAccount ClearingAccount { get; set; } = null!;
    public virtual BankAccount BankAccount { get; set; } = null!;
    public virtual JournalEntry? JournalEntry { get; set; }
    public virtual JournalEntry? ReversalJournalEntry { get; set; }
}

public static class SettlementBatchStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
    public const string Reversed = "reversed";
}
