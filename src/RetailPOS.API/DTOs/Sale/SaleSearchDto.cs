namespace RetailPOS.API.DTOs.Sale;

public class SaleSearchDto
{
    public long? OutletId { get; set; }
    public long? CashierId { get; set; }
    public long? CustomerId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
