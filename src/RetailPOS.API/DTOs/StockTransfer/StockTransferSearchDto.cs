namespace RetailPOS.API.DTOs.StockTransfer;

public class StockTransferSearchDto
{
    public string? SearchTerm { get; set; }
    public string? Status { get; set; }
    public long? FromLocationId { get; set; }
    public string? FromLocationType { get; set; }
    public long? ToLocationId { get; set; }
    public string? ToLocationType { get; set; }
    public long? ProductVariantId { get; set; }
    public string? TransferType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
