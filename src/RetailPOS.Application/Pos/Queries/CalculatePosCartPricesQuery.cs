using MediatR;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.Checkout;

namespace RetailPOS.Application.Pos.Queries;

public sealed record CalculatePosCartPricesQuery(
    IReadOnlyList<PosCartPriceLine> Items,
    bool ReadFromCache = false)
    : IQuery<IReadOnlyList<PosCartPriceReadModel>>;

public sealed class PosCartPriceCalculator(
    IPosCartPricingStore pricingStore,
    IPosCartPriceCache priceCache,
    ISaleCheckoutSettingsProvider checkoutSettings,
    IBusinessScope businessScope,
    IPosPricingCacheVersion cacheVersion) : IPosCartPriceCalculator
{
    public async Task<IReadOnlyList<PosCartPriceReadModel>> CalculateAsync(
        IReadOnlyList<PosCartPriceLine> items,
        bool readFromCache,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var taxPolicy = await checkoutSettings.GetTaxPolicyAsync(businessId, cancellationToken);
        // Prices cached under a different business, tax policy or pricing version must never be served. The version token
        // is read per outlet: an outlet override only orphans that outlet's entries, any catalogue change orphans them all.
        var scopePrefix =
            $"{businessId}:{(taxPolicy.Enabled ? 1 : 0)}:{taxPolicy.RatePercent}:{(taxPolicy.PricesIncludeTax ? 1 : 0)}";
        var cacheScopes = new Dictionary<long, string?>();
        foreach (var outletId in items.Select(item => item.OutletId).Distinct())
        {
            var token = await cacheVersion.GetTokenAsync(businessId, outletId, cancellationToken);
            cacheScopes[outletId.GetValueOrDefault()] = token is null ? null : $"{scopePrefix}:{token}";
        }

        var results = new PosCartPriceReadModel?[items.Count];
        var uncachedIndexes = new List<int>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[index];
            var cacheScope = cacheScopes[item.OutletId.GetValueOrDefault()];
            var cached = readFromCache && cacheScope is not null && item.Option is null
                ? await priceCache.GetAsync(
                    cacheScope,
                    item.ProductVariantId,
                    item.OutletId,
                    item.Quantity,
                    cancellationToken)
                : null;
            if (cached is not null)
            {
                results[index] = cached;
            }
            else
            {
                uncachedIndexes.Add(index);
            }
        }

        if (uncachedIndexes.Count == 0)
        {
            return results.Select(result => result!).ToArray();
        }

        var uncachedItems = uncachedIndexes.Select(index => items[index]).ToArray();
        var variantIds = uncachedItems
            .Select(item => item.ProductVariantId)
            .Distinct()
            .ToArray();
        var outletIds = uncachedItems
            .Where(item => item.OutletId.HasValue)
            .Select(item => item.OutletId!.Value)
            .Distinct()
            .ToArray();
        var asOfUtc = DateTime.UtcNow;

        var data = await pricingStore.LoadPricingDataAsync(
            variantIds,
            outletIds,
            asOfUtc,
            cancellationToken);

        var variants = data.Variants.ToDictionary(variant => variant.Id);
        var overrides = new Dictionary<(long OutletId, long ProductVariantId), PosCartPricingOverrideReadModel>();
        foreach (var priceOverride in data.Overrides)
        {
            overrides[(priceOverride.OutletId, priceOverride.ProductVariantId)] = priceOverride;
        }
        foreach (var index in uncachedIndexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[index];
            if (!variants.TryGetValue(item.ProductVariantId, out var variant))
            {
                throw new KeyNotFoundException(
                    $"ProductVariant with ID {item.ProductVariantId} not found");
            }

            if (item.Option is { } option)
            {
                results[index] = CalculateOptionPrice(variant, item.OutletId, option, data.Rules, taxPolicy);
                continue;
            }

            overrides.TryGetValue(
                (item.OutletId.GetValueOrDefault(), item.ProductVariantId),
                out var priceOverride);
            if (!item.OutletId.HasValue)
            {
                priceOverride = null;
            }

            var rule = data.Rules
                .Where(candidate =>
                    candidate.MinQuantity <= item.Quantity &&
                    Matches(candidate, variant, item.OutletId))
                .OrderByDescending(candidate => candidate.Priority)
                .ThenByDescending(candidate => candidate.Id)
                .FirstOrDefault();
            var result = CalculatePrice(variant, item.OutletId, priceOverride, rule, taxPolicy);
            results[index] = result;

            if (cacheScopes[item.OutletId.GetValueOrDefault()] is { } writeScope)
            {
                await priceCache.SetAsync(
                    writeScope,
                    item.ProductVariantId,
                    item.OutletId,
                    item.Quantity,
                    result,
                    cancellationToken);
            }
        }

        return results.Select(result => result!).ToArray();
    }

    private static bool Matches(
        PosCartPricingRuleReadModel rule,
        PosCartPricingVariantReadModel variant,
        long? outletId) =>
        (rule.RuleType == "variant" && rule.TargetId == variant.Id) ||
        (rule.RuleType == "product" && rule.TargetId == variant.ProductId) ||
        (rule.RuleType == "category" && rule.TargetId == variant.CategoryId) ||
        (outletId.HasValue && rule.RuleType == "outlet" && rule.TargetId == outletId.Value) ||
        rule.RuleType == "global";

    /// <summary>
    /// A rule's minimum and fixed amount were written for pieces. A pack is one countable piece, so both carry over: three
    /// 12 ml packs count as 3, never as 36. A measured quantity has no such count, so only a percentage rule with no real
    /// minimum applies to it. Any other rule is left out and named, and the sale is never mispriced by a guess.
    /// </summary>
    private static PosCartPriceReadModel CalculateOptionPrice(
        PosCartPricingVariantReadModel variant,
        long? outletId,
        UnitOptionPricing option,
        IReadOnlyList<PosCartPricingRuleReadModel> rules,
        SaleTaxPolicy taxPolicy)
    {
        var isPack = option.Kind == RetailPOS.Core.Entities.Uom.SellingOptionKinds.Pack;
        var listGross = isPack ? option.EnteredQuantity * option.Price : ToCurrency(option.EnteredQuantity * option.Price);
        var breakdown = new List<PosCartPriceBreakdownReadModel> { new("Option price", option.Price) };

        var candidates = option.ApplyAutomaticRules
            ? rules
                .Where(candidate => Matches(candidate, variant, outletId))
                .OrderByDescending(candidate => candidate.Priority)
                .ThenByDescending(candidate => candidate.Id)
                .ToArray()
            : [];
        bool Unambiguous(PosCartPricingRuleReadModel candidate) =>
            isPack || (candidate.DiscountType == "percentage" && candidate.MinQuantity <= 1);
        var rule = candidates.FirstOrDefault(candidate =>
            Unambiguous(candidate) && (!isPack || candidate.MinQuantity <= option.EnteredQuantity));
        var notApplied = candidates
            .Where(candidate => !Unambiguous(candidate))
            .Select(candidate => candidate.Name)
            .ToArray();

        var unitPrice = option.Price;
        var lineGross = listGross;
        var discount = 0m;
        if (rule is not null)
        {
            if (isPack)
            {
                discount = rule.DiscountType == "percentage"
                    ? ToCurrency(option.Price * rule.DiscountValue / 100m)
                    : rule.DiscountValue;
                discount = Math.Min(discount, option.Price);
                unitPrice = ToCurrency(option.Price - discount);
                lineGross = option.EnteredQuantity * unitPrice;
            }
            else
            {
                discount = Math.Min(ToCurrency(listGross * rule.DiscountValue / 100m), listGross);
                lineGross = listGross - discount;
            }

            breakdown.Add(new($"Discount: {rule.Name}", -discount));
        }

        var resolved = TaxResolver.Resolve(
            taxPolicy,
            new ProductTaxConfiguration(variant.TaxRate, variant.TaxClassification));
        var taxAmount = taxPolicy.PricesIncludeTax
            ? ToCurrency(lineGross * resolved.RatePercent / (100m + resolved.RatePercent))
            : ToCurrency(lineGross * resolved.RatePercent / 100m);
        breakdown.Add(new($"Tax ({resolved.RatePercent}%{(taxPolicy.PricesIncludeTax ? " included" : string.Empty)})", taxAmount));

        return new PosCartPriceReadModel(
            variant.Id,
            variant.ProductName,
            variant.VariantName,
            option.Price,
            option.Price,
            discount,
            unitPrice,
            resolved.RatePercent,
            taxAmount,
            taxPolicy.PricesIncludeTax ? lineGross : lineGross + taxAmount,
            false,
            rule?.Id,
            rule?.Name,
            breakdown,
            resolved.Classification,
            taxPolicy.PricesIncludeTax,
            lineGross,
            notApplied);
    }

    private static PosCartPriceReadModel CalculatePrice(
        PosCartPricingVariantReadModel variant,
        long? outletId,
        PosCartPricingOverrideReadModel? priceOverride,
        PosCartPricingRuleReadModel? appliedRule,
        SaleTaxPolicy taxPolicy)
    {
        var breakdown = new List<PosCartPriceBreakdownReadModel>();
        var basePrice = variant.ProductBasePrice + variant.VariantPriceAdjustment;
        breakdown.Add(new("Base price", basePrice));

        var outletPrice = basePrice;
        var overrideApplied = false;
        if (priceOverride is not null)
        {
            if (priceOverride.OverrideType == "fixed")
            {
                outletPrice = priceOverride.OverrideValue;
                breakdown.Add(new("Outlet fixed price", outletPrice));
            }
            else
            {
                var costBasis = variant.ProductCostPrice + variant.VariantCostAdjustment;
                outletPrice = ToCurrency(costBasis * (1m + priceOverride.OverrideValue / 100m));
                breakdown.Add(new($"Outlet margin {priceOverride.OverrideValue}% on cost", outletPrice));
            }

            overrideApplied = true;
        }

        var discount = 0m;
        if (appliedRule is not null)
        {
            discount = appliedRule.DiscountType == "percentage"
                ? ToCurrency(outletPrice * appliedRule.DiscountValue / 100m)
                : appliedRule.DiscountValue;
            discount = Math.Min(discount, outletPrice);
            breakdown.Add(new($"Discount: {appliedRule.Name}", -discount));
        }

        var finalPrice = ToCurrency(outletPrice - discount);
        var resolved = TaxResolver.Resolve(
            taxPolicy,
            new ProductTaxConfiguration(variant.TaxRate, variant.TaxClassification));
        // Inclusive prices already contain the tax, so the tax is extracted and the payable price is unchanged.
        var taxAmount = taxPolicy.PricesIncludeTax
            ? ToCurrency(finalPrice * resolved.RatePercent / (100m + resolved.RatePercent))
            : ToCurrency(finalPrice * resolved.RatePercent / 100m);
        breakdown.Add(new($"Tax ({resolved.RatePercent}%{(taxPolicy.PricesIncludeTax ? " included" : string.Empty)})", taxAmount));

        return new PosCartPriceReadModel(
            variant.Id,
            variant.ProductName,
            variant.VariantName,
            basePrice,
            outletPrice,
            discount,
            finalPrice,
            resolved.RatePercent,
            taxAmount,
            taxPolicy.PricesIncludeTax ? finalPrice : finalPrice + taxAmount,
            overrideApplied,
            appliedRule?.Id,
            appliedRule?.Name,
            breakdown,
            resolved.Classification,
            taxPolicy.PricesIncludeTax);
    }

    private static decimal ToCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class CalculatePosCartPricesQueryHandler(
    IPosCartPriceCalculator calculator,
    RetailPOS.Application.Uom.IVariantUomReader? uomReader = null,
    IBusinessScope? businessScope = null)
    : IRequestHandler<CalculatePosCartPricesQuery, IReadOnlyList<PosCartPriceReadModel>>
{
    public async Task<IReadOnlyList<PosCartPriceReadModel>> Handle(
        CalculatePosCartPricesQuery request,
        CancellationToken cancellationToken) =>
        await calculator.CalculateAsync(await WithOptionsAsync(request.Items, cancellationToken), request.ReadFromCache, cancellationToken);

    /// <summary>A line whose option is no longer current stays unpriced by option here; checkout refuses it with its own error.</summary>
    private async Task<IReadOnlyList<PosCartPriceLine>> WithOptionsAsync(
        IReadOnlyList<PosCartPriceLine> items,
        CancellationToken cancellationToken)
    {
        if (uomReader is null || businessScope is null || !items.Any(item => item.SellingOptionId.HasValue))
            return items;

        var variantIds = items
            .Where(item => item.SellingOptionId.HasValue)
            .Select(item => item.ProductVariantId)
            .Distinct()
            .ToList();
        var configs = await uomReader.GetCurrentAsync(businessScope.RequireBusinessId(), variantIds, cancellationToken);
        return items
            .Select(item =>
            {
                if (item.SellingOptionId is not { } optionId || item.EnteredQuantity is not > 0m)
                    return item;
                var option = configs.GetValueOrDefault(item.ProductVariantId)?.SellingOptions.FirstOrDefault(candidate => candidate.Id == optionId);
                return option is null
                    ? item
                    : item with { Option = new UnitOptionPricing(option.Kind, option.Price, item.EnteredQuantity.Value, option.ApplyAutomaticPricingRules) };
            })
            .ToArray();
    }
}
