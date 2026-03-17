namespace RetailPOS.API.DTOs.StockTransfer;

public class CreateStockTransferItemDto
{
    public long VariantId { get; set; }
    public int Quantity { get; set; }
}
