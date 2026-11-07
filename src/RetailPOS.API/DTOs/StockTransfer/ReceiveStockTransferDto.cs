namespace RetailPOS.API.DTOs.StockTransfer;

public class ReceiveStockTransferDto
{
    public List<ReceiveStockTransferItemDto> Items { get; set; } = new();
    public string? Notes { get; set; }
}

public class ReceiveStockTransferItemDto
{
    public long VariantId { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal MissingQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public string? Remarks { get; set; }
}
