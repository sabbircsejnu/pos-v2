namespace RetailPOS.API.DTOs.StockAdjustment;

public class StockAdjustmentListDto
{
    public List<StockAdjustmentDto> StockAdjustments { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
