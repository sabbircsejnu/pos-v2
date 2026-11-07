namespace RetailPOS.Core.Entities;

public class StockAdjustmentLine : RetailPOS.Core.Entities.Uom.UomSnapshotColumns
{
    public long Id { get; set; }
    public long StockAdjustmentId { get; set; }
    public long VariantId { get; set; }
    public decimal PreviousQuantity { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal NewQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    /// <summary>The physical stock this line changes when pack-state tracking is on. Null means loose.</summary>
    public string? PackState { get; set; }
    public string? PackCode { get; set; }
    public decimal? BaseQtyPerPack { get; set; }
    public decimal? UnitCostSnapshot { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual StockAdjustment StockAdjustment { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
}
