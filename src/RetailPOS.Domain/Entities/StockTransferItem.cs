namespace RetailPOS.Core.Entities;

public class StockTransferItem : RetailPOS.Core.Entities.Uom.UomSnapshotColumns
{
    public long Id { get; set; }
    public long TransferId { get; set; }
    public long VariantId { get; set; }
    // Legacy field kept for backward compatibility with existing service/UI.
    public decimal Quantity { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal TransferQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal MissingQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Remarks { get; set; }

    // Navigation properties
    public virtual StockTransfer Transfer { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
    public virtual ICollection<RetailPOS.Core.Entities.Uom.StockTransferItemAllocation> Allocations { get; set; } = new List<RetailPOS.Core.Entities.Uom.StockTransferItemAllocation>();
}
