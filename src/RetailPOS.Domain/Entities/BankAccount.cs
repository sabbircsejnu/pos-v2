namespace RetailPOS.Core.Entities;

public class BankAccount
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long ChartAccountId { get; set; }
    public long? OutletId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AccountReference { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ReconciliationStartDate { get; set; }
    public decimal? OpeningStatementBalance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual ChartAccount ChartAccount { get; set; } = null!;
    public virtual Outlet? Outlet { get; set; }
}
