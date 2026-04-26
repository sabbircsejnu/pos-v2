namespace RetailPOS.Core.Entities;

public class SaleItem
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal { get; set; }

    /// <summary>The pricing rule that was applied to this line at the time of sale. Null if no rule matched.</summary>
    public long? AppliedRuleId { get; set; }
    /// <summary>Monetary discount applied on this line (post-outlet-price, pre-tax). Captured for audit/reporting.</summary>
    public decimal DiscountAmount { get; set; } = 0;
    /// <summary>Snapshot of the rule name at sale time (avoids a join when printing receipts).</summary>
    public string? AppliedRuleName { get; set; }

    // Navigation properties
    public virtual Sale Sale { get; set; } = null!;
    public virtual ProductVariant Variant { get; set; } = null!;
}
