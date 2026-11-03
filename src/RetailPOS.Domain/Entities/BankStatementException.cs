namespace RetailPOS.Core.Entities;

public class BankStatementException
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long BankAccountId { get; set; }
    public long BankStatementLineId { get; set; }
    public string Classification { get; set; } = string.Empty;
    public string Status { get; set; } = BankStatementExceptionStatuses.Open;
    public string? Reason { get; set; }
    public string? ResolutionNotes { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public virtual Business Business { get; set; } = null!;
    public virtual BankAccount BankAccount { get; set; } = null!;
    public virtual BankStatementLine StatementLine { get; set; } = null!;
}

public static class BankStatementExceptionClassifications { public const string Chargeback = "chargeback"; public const string FailedSettlement = "failed_settlement"; }
public static class BankStatementExceptionStatuses { public const string Open = "open"; public const string Resolved = "resolved"; }
