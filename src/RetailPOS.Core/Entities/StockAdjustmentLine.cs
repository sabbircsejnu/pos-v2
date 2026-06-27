namespace RetailPOS.Core.Entities;

public class StockAdjustmentLine
{
    public long Id { get; set; }
    public long StockAdjustmentId { get; set; }
    public long VariantId { get; set; }
    public int PreviousQuantity { get; set; }
    public int QuantityChange { get; set; }
    public int NewQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual StockAdjustment StockAdjustment { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
}
