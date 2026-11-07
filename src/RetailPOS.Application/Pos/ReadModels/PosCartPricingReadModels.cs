namespace RetailPOS.Application.Pos.ReadModels;

public sealed record PosCartPriceLine(long ProductVariantId, long? OutletId, decimal Quantity, long? SellingOptionId = null, decimal? EnteredQuantity = null, UnitOptionPricing? Option = null);

/// <summary>A pack or custom quantity: the option's own price is what any rule discounts, and what counts toward a rule's minimum.</summary>
public sealed record UnitOptionPricing(string Kind, decimal Price, decimal EnteredQuantity, bool ApplyAutomaticRules);

public sealed record PosCartPricingVariantReadModel(
    long Id,
    long ProductId,
    long CategoryId,
    string ProductName,
    string VariantName,
    decimal ProductBasePrice,
    decimal VariantPriceAdjustment,
    decimal ProductCostPrice,
    decimal VariantCostAdjustment,
    decimal? TaxRate,
    string? TaxClassification = null,
    long BusinessId = 0);

public sealed record PosCartPricingOverrideReadModel(
    long OutletId,
    long ProductVariantId,
    string OverrideType,
    decimal OverrideValue);

public sealed record PosCartPricingRuleReadModel(
    long Id,
    int Priority,
    string Name,
    string RuleType,
    long? TargetId,
    string DiscountType,
    decimal DiscountValue,
    int MinQuantity);

public sealed record PosCartPricingDataReadModel(
    IReadOnlyList<PosCartPricingVariantReadModel> Variants,
    IReadOnlyList<PosCartPricingOverrideReadModel> Overrides,
    IReadOnlyList<PosCartPricingRuleReadModel> Rules);

public sealed record PosCartPriceBreakdownReadModel(string Label, decimal Amount);

public sealed record PosCartPriceReadModel(
    long ProductVariantId,
    string ProductName,
    string VariantName,
    decimal BasePrice,
    decimal OutletPrice,
    decimal DiscountAmount,
    decimal FinalPrice,
    decimal TaxRate,
    decimal TaxAmount,
    decimal FinalPriceWithTax,
    bool OutletOverrideApplied,
    long? AppliedRuleId,
    string? AppliedRuleName,
    IReadOnlyList<PosCartPriceBreakdownReadModel> Breakdown,
    string TaxClassification = "standard",
    bool PricesIncludeTax = false,
    decimal? LineGross = null,
    IReadOnlyList<string>? RulesNotApplied = null);
