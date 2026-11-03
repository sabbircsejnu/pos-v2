namespace RetailPOS.Core.Entities;

public class BankStatementImport
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long BankAccountId { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceFileChecksum { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = "csv-v1";
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StatementPeriodStart { get; set; }
    public DateTime? StatementPeriodEnd { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
    public int ImportedRowCount { get; set; }
    public int AcceptedRowCount { get; set; }
    public int RejectedRowCount { get; set; }
    public string Status { get; set; } = BankStatementImportStatuses.Pending;
    public string? ErrorSummary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual BankAccount BankAccount { get; set; } = null!;
    public virtual ICollection<BankStatementLine> Lines { get; set; } = new List<BankStatementLine>();
}

public static class BankStatementImportStatuses
{
    public const string Pending = "pending";
    public const string Imported = "imported";
    public const string Failed = "failed";
}
