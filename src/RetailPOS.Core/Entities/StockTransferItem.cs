namespace RetailPOS.Core.Entities;

public class StockTransferItem
{
    public long Id { get; set; }
    public long TransferId { get; set; }
    public long VariantId { get; set; }
    public int Quantity { get; set; }

    // Navigation properties
    public virtual StockTransfer Transfer { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
}
