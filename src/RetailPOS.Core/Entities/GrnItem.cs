namespace RetailPOS.Core.Entities;

public class GrnItem
{
    public long Id { get; set; }
    public long GrnId { get; set; }
    public long PoItemId { get; set; }
    public int ReceivedQty { get; set; }
    /// <summary>Actual unit cost at time of receipt (defaults to PO unit price).</summary>
    public decimal UnitCost { get; set; }                        // NEW
    /// <summary>Optional per-line notes (damage, batch info, etc.).</summary>
    public string? Notes { get; set; }                           // NEW

    // Navigation properties
    public virtual Grn Grn { get; set; } = null!;
    public virtual PurchaseOrderItem PurchaseOrderItem { get; set; } = null!;
}
