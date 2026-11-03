namespace RetailPOS.API.DTOs.StockTransfer;

public class StockTransferItemDto
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public string? ProductCode { get; set; }
    public string VariantSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? VariantAttributes { get; set; }
    public string? VariantDisplay { get; set; }
    public string? PrimaryImageThumb { get; set; }
    public int Quantity { get; set; }
    public int RequestedQuantity { get; set; }
    public int TransferQuantity { get; set; }
    public int AcceptedQuantity { get; set; }
    public int RejectedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Remarks { get; set; }
}
