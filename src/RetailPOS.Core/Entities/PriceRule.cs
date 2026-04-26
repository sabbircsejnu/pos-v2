namespace RetailPOS.Core.Entities;

/// <summary>
/// A campaign, promotion, or discount rule that reduces the effective price of matching items.
///
/// RuleType controls which entity TargetId refers to:
///   "variant"  → TargetId = ProductVariantId   (e.g. "Red XL shirt 20% off")
///   "product"  → TargetId = ProductId          (e.g. "All shirts 10% off")
///   "category" → TargetId = CategoryId         (e.g. "Footwear 15% off")
///   "outlet"   → TargetId = OutletId           (e.g. "Branch 2 storewide 5% off")
///   "global"   → TargetId = null               (e.g. "Ramadan 10% off everything")
///
/// DiscountType:
///   "percentage" → DiscountValue is a percentage (e.g. 10 = 10% off)
///   "fixed"      → DiscountValue is a monetary amount deducted from the outlet price
///
/// Rule priority: when multiple rules match a cart line, the one with
/// the highest Priority integer wins. On equal Priority the most
/// recently created rule (highest Id) is preferred.
///
/// ValidFrom / ValidTo are inclusive UTC boundaries. Null means open-ended.
/// MinQuantity gates the rule (e.g. "buy 3+ to qualify").
/// </summary>
public class PriceRule
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>"variant" | "product" | "category" | "outlet" | "global"</summary>
    public string RuleType { get; set; } = string.Empty;

    /// <summary>FK to the entity identified by RuleType. Always null when RuleType = "global".</summary>
    public long? TargetId { get; set; }

    /// <summary>"percentage" | "fixed"</summary>
    public string DiscountType { get; set; } = string.Empty;

    /// <summary>
    /// Percentage (1–100) or fixed monetary amount.
    /// Applied to the outlet-adjusted price (after OutletPriceOverride), not the base price.
    /// </summary>
    public decimal DiscountValue { get; set; }

    /// <summary>Minimum cart-line quantity required to activate this rule. Default 1.</summary>
    public int MinQuantity { get; set; } = 1;

    /// <summary>
    /// Tiebreaker when multiple rules match. Higher value = higher priority.
    /// Rules with the same Priority are broken by Id DESC (most recent wins).
    /// </summary>
    public int Priority { get; set; } = 0;

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
