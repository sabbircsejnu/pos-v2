namespace RetailPOS.Core.Entities;

public class BankReconciliationSession
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long BankAccountId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal? StatementOpeningBalance { get; set; }
    public decimal? StatementClosingBalance { get; set; }
    public decimal? GlOpeningBalance { get; set; }
    public string Status { get; set; } = BankReconciliationSessionStatuses.Open;
    public long? PreparedBy { get; set; }
    public DateTime? PreparedAt { get; set; }
    public long? ReconciledBy { get; set; }
    public DateTime? ReconciledAt { get; set; }
    public long? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal? ClosedDifference { get; set; }
    public string? CloseResultReference { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public long? ReopenedBy { get; set; }
    public string? ReopenReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual BankAccount BankAccount { get; set; } = null!;
    public virtual ICollection<BankReconciliationMatch> Matches { get; set; } = new List<BankReconciliationMatch>();
}

public static class BankReconciliationSessionStatuses
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string ReadyForClose = "ready_for_close";
    public const string Closed = "closed";
}
