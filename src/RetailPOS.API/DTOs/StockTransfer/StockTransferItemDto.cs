namespace RetailPOS.API.DTOs.StockTransfer;

public class StockTransferItemDto
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public string VariantSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
