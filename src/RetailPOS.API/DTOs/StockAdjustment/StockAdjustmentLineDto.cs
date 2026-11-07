namespace RetailPOS.API.DTOs.StockAdjustment;

public class StockAdjustmentLineDto
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantSku { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public decimal PreviousQuantity { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal NewQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? PackState { get; set; }
    public string? PackCode { get; set; }
    public decimal? BaseQtyPerPack { get; set; }
    public string? UomUnitCode { get; set; }
    public string? UomLabel { get; set; }
    public decimal? UomEnteredQuantity { get; set; }
    public long? UomOptionId { get; set; }
}
