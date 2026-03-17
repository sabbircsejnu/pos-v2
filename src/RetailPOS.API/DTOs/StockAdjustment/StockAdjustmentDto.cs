namespace RetailPOS.API.DTOs.StockAdjustment;

public class StockAdjustmentDto
{
    public long Id { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public long VariantId { get; set; }
    public string VariantSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int QuantityChange { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long AdjustedBy { get; set; }
    public string AdjusterName { get; set; } = string.Empty;
    public DateTime AdjustmentDate { get; set; }
}
