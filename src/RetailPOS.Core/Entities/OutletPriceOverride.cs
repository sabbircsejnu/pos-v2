namespace RetailPOS.Core.Entities;

/// <summary>
/// Sets a specific selling price (or cost-based margin) for one product variant at one outlet.
/// This REPLACES the standard base+adjustment price for that outlet; it is not a discount on top.
///
/// OverrideType:
///   "fixed"  → OverrideValue is the exact selling price charged at this outlet.
///   "margin" → OverrideValue is the target margin % above cost.
///              Effective price = (CostPrice + CostAdjustment) × (1 + OverrideValue / 100)
///
/// Design note:
///   OutletPriceOverride answers "what is the price of Variant X at Outlet Y?"
///   PriceRule (RuleType="outlet") answers "what discount does everything get at Outlet Y?"
///   Campaign rules are applied ON TOP of the outlet-adjusted price.
///
/// Unique constraint: one active override per (OutletId, ProductVariantId) is enforced at DB level.
/// </summary>
public class OutletPriceOverride
{
    public long Id { get; set; }
    public long OutletId { get; set; }
    public long ProductVariantId { get; set; }

    /// <summary>"fixed" | "margin"</summary>
    public string OverrideType { get; set; } = string.Empty;

    public decimal OverrideValue { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties (no inverse collections needed on Outlet/ProductVariant)
    public virtual Outlet Outlet { get; set; } = null!;
    public virtual ProductVariant ProductVariant { get; set; } = null!;
}
