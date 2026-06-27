namespace RetailPOS.API.DTOs.StockCount;

public class StockCountSearchDto
{
    public string? Search { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? Status { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
