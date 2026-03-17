namespace RetailPOS.Core.Entities;

public class SaleItem
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }

    // Navigation properties
    public virtual Sale Sale { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
}
