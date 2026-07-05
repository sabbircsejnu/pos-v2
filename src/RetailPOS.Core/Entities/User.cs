namespace RetailPOS.Core.Entities;

public class User
{
    public const string InventoryAccessAssignedOnly = "assigned_only";
    public const string InventoryAccessSpecific = "specific_locations";
    public const string InventoryAccessAll = "all_locations";

    public long Id { get; set; }
    public long? BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public long? RoleId { get; set; }
    public long? OutletId { get; set; }
    public string InventoryLocationAccessScope { get; set; } = InventoryAccessAssignedOnly;
    public bool IsActive { get; set; } = true;
    public bool MustResetPassword { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Business? Business { get; set; }
    public virtual Role? Role { get; set; }
    public virtual Outlet? Outlet { get; set; }
    public virtual ICollection<UserInvitation> Invitations { get; set; } = new List<UserInvitation>();
    public virtual ICollection<UserRefreshToken> RefreshTokens { get; set; } = new List<UserRefreshToken>();
    public virtual ICollection<UserWarehouseAssignment> WarehouseAssignments { get; set; } = new List<UserWarehouseAssignment>();
    public virtual ICollection<UserOutletAssignment> OutletAssignments { get; set; } = new List<UserOutletAssignment>();
    public virtual ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
    public virtual ICollection<Grn> Grns { get; set; } = new List<Grn>();
    public virtual ICollection<StockTransfer> StockTransfersCreated { get; set; } = new List<StockTransfer>();
    public virtual ICollection<StockTransfer> StockTransfersApproved { get; set; } = new List<StockTransfer>();
    public virtual ICollection<StockAdjustment> StockAdjustments { get; set; } = new List<StockAdjustment>();
    public virtual ICollection<StockAdjustment> StockAdjustmentsApproved { get; set; } = new List<StockAdjustment>();
    public virtual ICollection<StockAdjustment> StockAdjustmentsRejected { get; set; } = new List<StockAdjustment>();
    public virtual ICollection<StockAdjustment> StockAdjustmentsCancelled { get; set; } = new List<StockAdjustment>();
    public virtual ICollection<ReceiptPrintHistory> ReceiptPrintHistories { get; set; } = new List<ReceiptPrintHistory>();
    public virtual ICollection<SaleVoid> SaleVoids { get; set; } = new List<SaleVoid>();
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
