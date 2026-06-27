namespace RetailPOS.API.DTOs.StockTransfer;

public class CreateStockTransferItemDto
{
    public long VariantId { get; set; }
    public int Quantity { get; set; }
    public int? RequestedQuantity { get; set; }
    public int? TransferQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Remarks { get; set; }
}
