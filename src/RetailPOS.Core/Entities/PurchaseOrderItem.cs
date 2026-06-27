namespace RetailPOS.Core.Entities;

public class PurchaseOrderItem
{
    public long Id { get; set; }
    public long PoId { get; set; }
    public long VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Item-level discount percentage (0–100). Default: 0.</summary>
    public decimal Discount { get; set; } = 0;
    /// <summary>Item-level tax percentage (0–100). Default: 0.</summary>
    public decimal Tax { get; set; } = 0;
    /// <summary>Unit of measurement (e.g. pcs, kg, box). Nullable.</summary>
    public string? Unit { get; set; }

    // Navigation properties
    public virtual PurchaseOrder PurchaseOrder { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
    public virtual ICollection<GrnItem> GrnItems { get; set; } = new List<GrnItem>();
}
