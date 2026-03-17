namespace RetailPOS.API.DTOs.StockAdjustment;

public class CreateStockAdjustmentDto
{
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public long VariantId { get; set; }
    public int QuantityChange { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
