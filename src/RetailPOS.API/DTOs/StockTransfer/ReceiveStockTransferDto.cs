namespace RetailPOS.API.DTOs.StockTransfer;

public class ReceiveStockTransferDto
{
    public List<ReceiveStockTransferItemDto> Items { get; set; } = new();
    public string? Notes { get; set; }
}

public class ReceiveStockTransferItemDto
{
    public long VariantId { get; set; }
    public int AcceptedQuantity { get; set; }
    public int RejectedQuantity { get; set; }
    public string? Remarks { get; set; }
}
