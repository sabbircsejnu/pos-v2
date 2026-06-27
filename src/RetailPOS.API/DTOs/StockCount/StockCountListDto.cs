namespace RetailPOS.API.DTOs.StockCount;

public class StockCountListDto
{
    public List<StockCountDto> StockCounts { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
