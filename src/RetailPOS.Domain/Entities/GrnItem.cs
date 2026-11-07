namespace RetailPOS.Core.Entities;

public class GrnItem : RetailPOS.Core.Entities.Uom.UomSnapshotColumns
{
    public long Id { get; set; }
    public long GrnId { get; set; }
    public long PoItemId { get; set; }
    public decimal ReceivedQty { get; set; }
    /// <summary>Actual unit cost at time of receipt (defaults to PO unit price).</summary>
    public decimal UnitCost { get; set; }                        // NEW
    public decimal? TaxableAmount { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public string? TaxCode { get; set; }
    public bool? TaxInclusive { get; set; }
    /// <summary>Physically received but not accepted: not stocked and not payable (D11). Evidence for a supplier claim.</summary>
    public decimal DamagedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    /// <summary>Whole sealed packs accepted, when pack-state tracking is on.</summary>
    public int? AcceptedSealedPacks { get; set; }
    public decimal? AcceptedLooseQuantity { get; set; }
    /// <summary>Optional per-line notes (damage, batch info, etc.).</summary>
    public string? Notes { get; set; }                           // NEW

    // Navigation properties
    public virtual Grn Grn { get; set; } = null!;
    public virtual PurchaseOrderItem PurchaseOrderItem { get; set; } = null!;
}
