namespace RetailPOS.Core.Entities;

public class LiquidityTransfer
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string TransferType { get; set; } = string.Empty;
    public long SourceAccountId { get; set; }
    public long DestinationAccountId { get; set; }
    public long? SourceBankAccountId { get; set; }
    public long? DestinationBankAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PostingDate { get; set; }
    public string? Reference { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = LiquidityTransferStatuses.Draft;
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
    public virtual ChartAccount SourceAccount { get; set; } = null!;
    public virtual ChartAccount DestinationAccount { get; set; } = null!;
    public virtual BankAccount? SourceBankAccount { get; set; }
    public virtual BankAccount? DestinationBankAccount { get; set; }
    public virtual JournalEntry? JournalEntry { get; set; }
    public virtual JournalEntry? ReversalJournalEntry { get; set; }
}

public static class LiquidityTransferTypes
{
    public const string CashToBank = "cash_to_bank";
    public const string BankToCash = "bank_to_cash";
    public const string BankToBank = "bank_to_bank";
}

public static class LiquidityTransferStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
    public const string Reversed = "reversed";
}
