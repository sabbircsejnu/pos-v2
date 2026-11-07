using System.ComponentModel.DataAnnotations;

namespace RetailPOS.API.DTOs.Pricing;

// ──────────────────────────────────────────────────────────────────────────────
//  Price Calculation
// ──────────────────────────────────────────────────────────────────────────────

public class PriceCalculationRequestDto
{
    [Required]
    public long ProductVariantId { get; set; }

    /// <summary>
    /// The outlet context for price resolution.
    /// Pass null to get the base price (no outlet override applied).
    /// </summary>
    public long? OutletId { get; set; }

    /// <summary>
    /// Cart-line quantity. Used to evaluate MinQuantity campaign rules. Default 1.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
    public int Quantity { get; set; } = 1;

    /// <summary>A pack or custom option of the variant. It is priced from the option, using <see cref="EnteredQuantity"/>.</summary>
    public long? SellingOptionId { get; set; }

    /// <summary>The pack count or measured quantity entered for <see cref="SellingOptionId"/>.</summary>
    public decimal? EnteredQuantity { get; set; }
}

/// <summary>Batch version for POS — avoids N+1 HTTP calls when building a cart.</summary>
public class PriceCalculationBatchRequestDto
{
    [Required]
    [MinLength(1, ErrorMessage = "At least one item is required")]
    [MaxLength(500, ErrorMessage = "Maximum 500 items per batch")]
    public List<PriceCalculationRequestDto> Items { get; set; } = new();
}

public class PriceCalculationResultDto
{
    /// <summary>The whole line's amount for a pack or custom option, before manual discount. Null for a plain line.</summary>
    public decimal? LineGross { get; set; }

    /// <summary>Rules that match this option but cannot be applied to a measured quantity without guessing.</summary>
    public List<string>? RulesNotApplied { get; set; }

    public long ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;

    /// <summary>Product.BasePrice + Variant.PriceAdjustment</summary>
    public decimal BasePrice { get; set; }

    /// <summary>After outlet override (equals BasePrice when no active override exists).</summary>
    public decimal OutletPrice { get; set; }

    /// <summary>Monetary discount applied by the winning campaign rule.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>OutletPrice − DiscountAmount. The register line-item price.</summary>
    public decimal FinalPrice { get; set; }

    /// <summary>Effective tax rate (percent) resolved from the product, its classification and the Business policy. Not added to FinalPrice when <see cref="PricesIncludeTax"/>.</summary>
    public decimal TaxRate { get; set; }

    /// <summary>standard | zero_rated | exempt | none (Business tax switched off).</summary>
    public string TaxClassification { get; set; } = "standard";

    /// <summary>True when selling prices already include tax (FinalPriceWithTax equals FinalPrice).</summary>
    public bool PricesIncludeTax { get; set; }

    /// <summary>Per-unit tax: FinalPrice x rate / 100 (exclusive) or extracted from FinalPrice (inclusive).</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>FinalPrice + TaxAmount for convenience.</summary>
    public decimal FinalPriceWithTax { get; set; }

    public bool OutletOverrideApplied { get; set; }
    public long? AppliedRuleId { get; set; }
    public string? AppliedRuleName { get; set; }

    /// <summary>Step-by-step breakdown for display in POS, receipt, and admin UI.</summary>
    public List<PriceBreakdownLineDto> Breakdown { get; set; } = new();
}

public class PriceBreakdownLineDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────────
//  Price Rules (campaign / discount rules)
// ──────────────────────────────────────────────────────────────────────────────

public class PriceRuleDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"variant" | "product" | "category" | "outlet" | "global"</summary>
    public string RuleType { get; set; } = string.Empty;

    public long? TargetId { get; set; }

    /// <summary>"percentage" | "fixed"</summary>
    public string DiscountType { get; set; } = string.Empty;

    public decimal DiscountValue { get; set; }
    public int MinQuantity { get; set; }
    public int Priority { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePriceRuleDto
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// "variant" | "product" | "category" | "outlet" | "global"
    /// TargetId must be supplied for all types except "global".
    /// </summary>
    [Required]
    public string RuleType { get; set; } = string.Empty;

    public long? TargetId { get; set; }

    /// <summary>"percentage" | "fixed"</summary>
    [Required]
    public string DiscountType { get; set; } = string.Empty;

    /// <summary>1–100 for "percentage"; positive amount for "fixed". Must be > 0.</summary>
    [Required, Range(0.01, double.MaxValue, ErrorMessage = "DiscountValue must be greater than zero")]
    public decimal DiscountValue { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "MinQuantity must be at least 1")]
    public int MinQuantity { get; set; } = 1;

    public int Priority { get; set; } = 0;

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdatePriceRuleDto : CreatePriceRuleDto { }

public class PriceRuleListDto
{
    public List<PriceRuleDto> Rules { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}

public class PriceRuleSearchDto
{
    public string? SearchQuery { get; set; }
    public string? RuleType { get; set; }
    public bool? IsActive { get; set; }

    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

// ──────────────────────────────────────────────────────────────────────────────
//  Outlet Price Overrides
// ──────────────────────────────────────────────────────────────────────────────

public class OutletPriceOverrideDto
{
    public long Id { get; set; }
    public long OutletId { get; set; }
    public string OutletName { get; set; } = string.Empty;
    public long ProductVariantId { get; set; }
    public string VariantName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    /// <summary>"fixed" | "margin"</summary>
    public string OverrideType { get; set; } = string.Empty;

    public decimal OverrideValue { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateOutletPriceOverrideDto
{
    [Required]
    public long OutletId { get; set; }

    [Required]
    public long ProductVariantId { get; set; }

    /// <summary>"fixed" | "margin"</summary>
    [Required]
    public string OverrideType { get; set; } = string.Empty;

    [Required, Range(0.01, double.MaxValue, ErrorMessage = "OverrideValue must be greater than zero")]
    public decimal OverrideValue { get; set; }

    public bool IsActive { get; set; } = true;
}

public class UpdateOutletPriceOverrideDto
{
    /// <summary>"fixed" | "margin"</summary>
    [Required]
    public string OverrideType { get; set; } = string.Empty;

    [Required, Range(0.01, double.MaxValue, ErrorMessage = "OverrideValue must be greater than zero")]
    public decimal OverrideValue { get; set; }

    public bool IsActive { get; set; } = true;
}

public class OutletPriceOverrideListDto
{
    public List<OutletPriceOverrideDto> Overrides { get; set; } = new();
    public int TotalCount { get; set; }
}
