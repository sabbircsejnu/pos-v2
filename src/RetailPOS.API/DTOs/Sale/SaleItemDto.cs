namespace RetailPOS.API.DTOs.Sale;

public class SaleItemDto
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantSku { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? HistoricalUnitCost { get; set; }
    public decimal DiscountAmount { get; set; }      // UPDATED — line-level discount
    public decimal Subtotal { get; set; }
    public string? AppliedRuleName { get; set; }     // UPDATED — winning discount rule name
    public string? UnitCode { get; set; }
    public string? UnitLabel { get; set; }
    public decimal? EnteredQuantity { get; set; }
    public decimal? EnteredUnitPrice { get; set; }
    public string? BaseUnitCode { get; set; }
    public string? PackState { get; set; }
    public decimal? ReturnedQuantity { get; set; }
}
