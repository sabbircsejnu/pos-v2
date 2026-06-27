namespace RetailPOS.Core.Entities;

public class StockTransferItem
{
    public long Id { get; set; }
    public long TransferId { get; set; }
    public long VariantId { get; set; }
    // Legacy field kept for backward compatibility with existing service/UI.
    public int Quantity { get; set; }
    public int RequestedQuantity { get; set; }
    public int TransferQuantity { get; set; }
    public int AcceptedQuantity { get; set; }
    public int RejectedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Remarks { get; set; }

    // Navigation properties
    public virtual StockTransfer Transfer { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
}
