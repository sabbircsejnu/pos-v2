namespace RetailPOS.API.DTOs.StockAdjustment;

public class StockAdjustmentSearchDto
{
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
    public long? VariantId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
