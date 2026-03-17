namespace RetailPOS.API.DTOs.StockTransfer;

public class StockTransferSearchDto
{
    public string? Status { get; set; }
    public long? FromLocationId { get; set; }
    public string? FromLocationType { get; set; }
    public long? ToLocationId { get; set; }
    public string? ToLocationType { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
