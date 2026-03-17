namespace RetailPOS.API.DTOs.Sale;

public class SaleListDto
{
    public List<SaleDto> Sales { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
