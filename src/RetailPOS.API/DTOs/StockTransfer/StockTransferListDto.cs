namespace RetailPOS.API.DTOs.StockTransfer;

public class StockTransferListDto
{
    public List<StockTransferDto> StockTransfers { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}
