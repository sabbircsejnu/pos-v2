namespace RetailPOS.Core.Entities;

public class StockAdjustment
{
    public long Id { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public long VariantId { get; set; }
    public int QuantityChange { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long AdjustedBy { get; set; }
    public DateTime AdjustmentDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ProductVariant Variant { get; set; } = null!;
    public virtual User Adjuster { get; set; } = null!;
}
