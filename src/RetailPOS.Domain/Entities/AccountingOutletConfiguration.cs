namespace RetailPOS.Core.Entities;

public class AccountingOutletConfiguration
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long OutletId { get; set; }

    public long? OutletCashAccountId { get; set; }
    public long? OutletPettyCashAccountId { get; set; }
    public long? OutletBankAccountId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Outlet Outlet { get; set; } = null!;
    public virtual ChartAccount? OutletCashAccount { get; set; }
    public virtual ChartAccount? OutletPettyCashAccount { get; set; }
    public virtual ChartAccount? OutletBankAccount { get; set; }
}
