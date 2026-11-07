namespace RetailPOS.Core.Entities;

public class StockRequisitionLine
{
    public long Id { get; set; }
    public long RequisitionId { get; set; }
    public long VariantId { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal FulfilledQuantity { get; set; }
    public string? Remarks { get; set; }

    // Navigation properties
    public virtual StockRequisition Requisition { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
}
