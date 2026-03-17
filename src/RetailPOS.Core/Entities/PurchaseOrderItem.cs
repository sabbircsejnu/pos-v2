namespace RetailPOS.Core.Entities;

public class PurchaseOrderItem
{
    public long Id { get; set; }
    public long PoId { get; set; }
    public long VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    // Navigation properties
    public virtual PurchaseOrder PurchaseOrder { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
    public virtual ICollection<GrnItem> GrnItems { get; set; } = new List<GrnItem>();
}
