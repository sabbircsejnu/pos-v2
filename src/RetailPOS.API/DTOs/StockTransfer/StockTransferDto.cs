namespace RetailPOS.API.DTOs.StockTransfer;

public class StockTransferDto
{
    public long Id { get; set; }
    public long FromLocationId { get; set; }
    public string FromLocationType { get; set; } = string.Empty;
    public string FromLocationName { get; set; } = string.Empty;
    public long ToLocationId { get; set; }
    public string ToLocationType { get; set; } = string.Empty;
    public string ToLocationName { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? ApprovedBy { get; set; }
    public string? ApproverName { get; set; }
    public long? CreatedBy { get; set; }
    public string? CreatorName { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<StockTransferItemDto> Items { get; set; } = new();
}
