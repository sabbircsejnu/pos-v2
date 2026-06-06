namespace RetailPOS.Core.Entities;

public class User
{
    public long Id { get; set; }
    public long? BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public long? RoleId { get; set; }
    public long? OutletId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustResetPassword { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Business? Business { get; set; }
    public virtual Role? Role { get; set; }
    public virtual Outlet? Outlet { get; set; }
    public virtual ICollection<UserInvitation> Invitations { get; set; } = new List<UserInvitation>();
    public virtual ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
    public virtual ICollection<Grn> Grns { get; set; } = new List<Grn>();
    public virtual ICollection<StockTransfer> StockTransfersCreated { get; set; } = new List<StockTransfer>();
    public virtual ICollection<StockTransfer> StockTransfersApproved { get; set; } = new List<StockTransfer>();
    public virtual ICollection<StockAdjustment> StockAdjustments { get; set; } = new List<StockAdjustment>();
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
