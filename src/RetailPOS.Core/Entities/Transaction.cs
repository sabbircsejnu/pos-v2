namespace RetailPOS.Core.Entities;

public class Transaction
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty; // debit, credit
    public string? Description { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    public long? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }

    // Navigation properties
    public virtual Account Account { get; set; } = null!;
}
