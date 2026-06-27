namespace RetailPOS.API.DTOs.StockTransfer;

public class CreateStockTransferDto
{
    public string TransferType { get; set; } = "direct";
    public long? RelatedRequisitionId { get; set; }
    public long FromLocationId { get; set; }
    public string FromLocationType { get; set; } = string.Empty;
    public long ToLocationId { get; set; }
    public string ToLocationType { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public string? Notes { get; set; }
    public List<CreateStockTransferItemDto> Items { get; set; } = new();
}
