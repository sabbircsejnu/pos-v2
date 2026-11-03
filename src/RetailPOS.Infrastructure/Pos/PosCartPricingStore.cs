using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pos;

public sealed class PosCartPricingStore(RetailPOSDbContext dbContext, IBusinessScope businessScope) : IPosCartPricingStore
{
    public async Task<PosCartPricingDataReadModel> LoadPricingDataAsync(
        IReadOnlyCollection<long> variantIds,
        IReadOnlyCollection<long> outletIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var variantIdList = variantIds.ToList();
        var outletIdList = outletIds.ToList();
        if (outletIdList.Count > 0)
        {
            outletIdList = await dbContext.Outlets
                .AsNoTracking()
                .Where(outlet => outlet.BusinessId == businessId && outletIdList.Contains(outlet.Id))
                .Select(outlet => outlet.Id)
                .ToListAsync(cancellationToken);
        }

        var variants = await dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.BusinessId == businessId && variantIdList.Contains(variant.Id))
            .Select(variant => new PosCartPricingVariantReadModel(
                variant.Id,
                variant.ProductId,
                variant.Product.CategoryId,
                variant.Product.Name,
                variant.Name,
                variant.Product.BasePrice,
                variant.PriceAdjustment,
                variant.Product.CostPrice,
                variant.CostAdjustment,
                variant.Product.TaxRate,
                variant.Product.TaxClassification,
                variant.BusinessId))
            .ToListAsync(cancellationToken);

        var overrides = outletIdList.Count == 0
            ? []
            : await dbContext.OutletPriceOverrides
                .AsNoTracking()
                .Where(priceOverride =>
                    priceOverride.BusinessId == businessId &&
                    outletIdList.Contains(priceOverride.OutletId) &&
                    variantIdList.Contains(priceOverride.ProductVariantId) &&
                    priceOverride.IsActive)
                .Select(priceOverride => new PosCartPricingOverrideReadModel(
                    priceOverride.OutletId,
                    priceOverride.ProductVariantId,
                    priceOverride.OverrideType,
                    priceOverride.OverrideValue))
                .ToListAsync(cancellationToken);

        var productIds = variants.Select(variant => variant.ProductId).Distinct().ToList();
        var categoryIds = variants.Select(variant => variant.CategoryId).Distinct().ToList();
        var rules = await dbContext.PriceRules
            .AsNoTracking()
            .Where(rule =>
                rule.BusinessId == businessId &&
                rule.IsActive &&
                (rule.ValidFrom == null || rule.ValidFrom <= asOfUtc) &&
                (rule.ValidTo == null || rule.ValidTo >= asOfUtc) &&
                (
                    (rule.RuleType == "variant" && rule.TargetId.HasValue && variantIdList.Contains(rule.TargetId.Value)) ||
                    (rule.RuleType == "product" && rule.TargetId.HasValue && productIds.Contains(rule.TargetId.Value)) ||
                    (rule.RuleType == "category" && rule.TargetId.HasValue && categoryIds.Contains(rule.TargetId.Value)) ||
                    (outletIdList.Count > 0 && rule.RuleType == "outlet" &&
                        rule.TargetId.HasValue && outletIdList.Contains(rule.TargetId.Value)) ||
                    rule.RuleType == "global"))
            .OrderByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.Id)
            .Select(rule => new PosCartPricingRuleReadModel(
                rule.Id,
                rule.Priority,
                rule.Name,
                rule.RuleType,
                rule.TargetId,
                rule.DiscountType,
                rule.DiscountValue,
                rule.MinQuantity))
            .ToListAsync(cancellationToken);

        return new PosCartPricingDataReadModel(variants, overrides, rules);
    }
}
