namespace RetailPOS.Core.Entities;

public class PosTerminal
{
    public long Id { get; set; }
    public long OutletId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Outlet Outlet { get; set; } = null!;
    public virtual ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public virtual ICollection<ReceiptPrintHistory> ReceiptPrintHistories { get; set; } = new List<ReceiptPrintHistory>();
}
