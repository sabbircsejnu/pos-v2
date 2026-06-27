namespace RetailPOS.API.DTOs.StockAdjustment;

public class UpdateStockAdjustmentDto
{
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public List<CreateStockAdjustmentLineDto> Items { get; set; } = new();
}