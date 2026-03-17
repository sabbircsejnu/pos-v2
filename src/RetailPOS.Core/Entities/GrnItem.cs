namespace RetailPOS.Core.Entities;

public class GrnItem
{
    public long Id { get; set; }
    public long GrnId { get; set; }
    public long PoItemId { get; set; }
    public int ReceivedQty { get; set; }

    // Navigation properties
    public virtual Grn Grn { get; set; } = null!;
    public virtual PurchaseOrderItem PurchaseOrderItem { get; set; } = null!;
}
