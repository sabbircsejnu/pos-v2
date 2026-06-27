namespace RetailPOS.API.DTOs.StockAdjustment;

public class StockAdjustmentLineDto
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantSku { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public int PreviousQuantity { get; set; }
    public int QuantityChange { get; set; }
    public int NewQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
