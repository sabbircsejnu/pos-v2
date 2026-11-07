namespace RetailPOS.Core.Entities;

public class SaleItem : RetailPOS.Core.Entities.Uom.UomSnapshotColumns
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long VariantId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }
    public decimal? TaxableAmount { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public string? TaxCode { get; set; }

    /// <summary>Tax classification applied to this line when it was sold: standard, zero_rated, exempt or none. Immutable snapshot.</summary>
    public string? TaxClassification { get; set; }
    public bool? TaxInclusive { get; set; }

    /// <summary>Historical unit cost captured at the time the sale was completed.</summary>
    public decimal? HistoricalUnitCost { get; private set; }

    /// <summary>The physical form sold when pack-state tracking is on: loose, sealed or prepared.</summary>
    public string? PackState { get; set; }
    public string? PackCode { get; set; }
    public decimal? BaseQtyPerPack { get; set; }
    /// <summary>Packaging cost carried by this line: consumed now, or capitalized in the prepared packs sold. Part of COGS.</summary>
    public decimal PackagingCost { get; set; }

    /// <summary>The pricing rule that was applied to this line at the time of sale. Null if no rule matched.</summary>
    public long? AppliedRuleId { get; set; }
    /// <summary>Monetary discount applied on this line (post-outlet-price, pre-tax). Captured for audit/reporting.</summary>
    public decimal DiscountAmount { get; set; } = 0;
    /// <summary>Snapshot of the rule name at sale time (avoids a join when printing receipts).</summary>
    public string? AppliedRuleName { get; set; }

    // Navigation properties
    public virtual Sale Sale { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
    public virtual ICollection<RetailPOS.Core.Entities.Uom.SaleReturnLine> ReturnLines { get; set; } = new List<RetailPOS.Core.Entities.Uom.SaleReturnLine>();
    public virtual ICollection<RetailPOS.Core.Entities.Uom.SaleItemComponent> Components { get; set; } = new List<RetailPOS.Core.Entities.Uom.SaleItemComponent>();

    public void CaptureHistoricalUnitCost(decimal unitCost)
    {
        if (HistoricalUnitCost.HasValue)
            throw new InvalidOperationException("Historical unit cost has already been captured for this sale line.");

        HistoricalUnitCost = unitCost;
    }
}
