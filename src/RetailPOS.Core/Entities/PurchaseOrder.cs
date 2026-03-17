namespace RetailPOS.Core.Entities;

public class PurchaseOrder
{
    public long Id { get; set; }
    public long SupplierId { get; set; }
    public long WarehouseId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDelivery { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "pending"; // pending, approved, received, cancelled
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Supplier Supplier { get; set; } = null!;
    public virtual Warehouse Warehouse { get; set; } = null!;
    public virtual User? Creator { get; set; }
    public virtual ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    public virtual ICollection<Grn> Grns { get; set; } = new List<Grn>();
    public virtual ICollection<Bill> Bills { get; set; } = new List<Bill>();
}
