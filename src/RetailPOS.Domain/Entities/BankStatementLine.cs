namespace RetailPOS.Core.Entities;

public class BankStatementLine
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long BankAccountId { get; set; }
    public long BankStatementImportId { get; set; }
    public int SourceRowNumber { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime? ValueDate { get; set; }
    public string? ExternalTransactionReference { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal SignedAmount { get; set; }
    public decimal? RunningBalance { get; set; }
    public string? Currency { get; set; }
    public string? NormalizedSearchText { get; set; }
    public string? RawReferenceText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Business Business { get; set; } = null!;
    public virtual BankAccount BankAccount { get; set; } = null!;
    public virtual BankStatementImport Import { get; set; } = null!;
}
