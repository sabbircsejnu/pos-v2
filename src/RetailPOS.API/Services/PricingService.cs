using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Pricing;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

public class PricingService : IPricingService
{
    private readonly IPriceRuleRepository _ruleRepo;
    private readonly IOutletPriceOverrideRepository _overrideRepo;
    private readonly RetailPOSDbContext _context;
    private readonly ILogger<PricingService> _logger;

    // UPDATED: added IPosCacheService for Redis price caching
    private readonly IPosCacheService _posCache;

    private static readonly HashSet<string> ValidRuleTypes =
        new(StringComparer.OrdinalIgnoreCase) { "variant", "product", "category", "outlet", "global" };

    private static readonly HashSet<string> ValidDiscountTypes =
        new(StringComparer.OrdinalIgnoreCase) { "percentage", "fixed" };

    private static readonly HashSet<string> ValidOverrideTypes =
        new(StringComparer.OrdinalIgnoreCase) { "fixed", "margin" };

    public PricingService(
        IPriceRuleRepository ruleRepo,
        IOutletPriceOverrideRepository overrideRepo,
        RetailPOSDbContext context,
        ILogger<PricingService> logger,
        IPosCacheService posCache) // UPDATED
    {
        _ruleRepo     = ruleRepo;
        _overrideRepo = overrideRepo;
        _context      = context;
        _logger       = logger;
        _posCache     = posCache; // UPDATED
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Core calculation
    // ──────────────────────────────────────────────────────────────────────

    public async Task<PriceCalculationResultDto> CalculateAsync(PriceCalculationRequestDto request)
    {
        // UPDATED: check Redis cache before hitting the DB
        var cached = await _posCache.GetPriceAsync(request.ProductVariantId, request.OutletId, request.Quantity);
        if (cached is not null) return cached;

        var variant = await _context.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == request.ProductVariantId)
            ?? throw new KeyNotFoundException($"ProductVariant with ID {request.ProductVariantId} not found");

        var result = await ResolvePrice(variant, request.OutletId, request.Quantity, DateTime.UtcNow);
        await _posCache.SetPriceAsync(request.ProductVariantId, request.OutletId, request.Quantity, result);
        return result;
    }

    public async Task<List<PriceCalculationResultDto>> CalculateBatchAsync(
        PriceCalculationBatchRequestDto request)
    {
        var variantIds = request.Items.Select(i => i.ProductVariantId).Distinct().ToList();
        var outletIds  = request.Items
            .Where(i => i.OutletId.HasValue)
            .Select(i => i.OutletId!.Value)
            .Distinct()
            .ToList();

        var now = DateTime.UtcNow;

        // 1 query for all needed variants
        var variants = await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id);

        // 1 query for all active overrides covering these (outlet × variant) pairs
        var overrideMap = new Dictionary<(long, long), OutletPriceOverride>();
        if (outletIds.Count > 0)
        {
            var overrides = await _context.OutletPriceOverrides
                .Where(o => outletIds.Contains(o.OutletId) &&
                            variantIds.Contains(o.ProductVariantId) &&
                            o.IsActive)
                .ToListAsync();
            foreach (var o in overrides)
                overrideMap[(o.OutletId, o.ProductVariantId)] = o;
        }

        // 1 query for all potentially matching rules; per-item MinQuantity filter applied in memory
        var productIds  = variants.Values.Select(v => v.ProductId).Distinct().ToList();
        var categoryIds = variants.Values.Select(v => v.Product.CategoryId).Distinct().ToList();

        var rulePool = await _context.PriceRules
            .Where(r =>
                r.IsActive &&
                (r.ValidFrom == null || r.ValidFrom <= now) &&
                (r.ValidTo   == null || r.ValidTo   >= now) &&
                (
                    (r.RuleType == "variant"  && r.TargetId.HasValue && variantIds.Contains(r.TargetId.Value))  ||
                    (r.RuleType == "product"  && r.TargetId.HasValue && productIds.Contains(r.TargetId.Value))  ||
                    (r.RuleType == "category" && r.TargetId.HasValue && categoryIds.Contains(r.TargetId.Value)) ||
                    (outletIds.Count > 0 && r.RuleType == "outlet" && r.TargetId.HasValue && outletIds.Contains(r.TargetId.Value)) ||
                    r.RuleType == "global"
                ))
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.Id)
            .ToListAsync();

        var results = new List<PriceCalculationResultDto>(request.Items.Count);

        foreach (var item in request.Items)
        {
            if (!variants.TryGetValue(item.ProductVariantId, out var variant))
                throw new KeyNotFoundException($"ProductVariant with ID {item.ProductVariantId} not found");

            overrideMap.TryGetValue((item.OutletId.GetValueOrDefault(), item.ProductVariantId), out var opo);
            if (!item.OutletId.HasValue) opo = null; // ignore override hits when no outlet was specified

            var bestRule = rulePool.FirstOrDefault(r =>
                r.MinQuantity <= item.Quantity &&
                (
                    (r.RuleType == "variant"  && r.TargetId == variant.Id)                         ||
                    (r.RuleType == "product"  && r.TargetId == variant.ProductId)                  ||
                    (r.RuleType == "category" && r.TargetId == variant.Product.CategoryId)          ||
                    (item.OutletId.HasValue && r.RuleType == "outlet" && r.TargetId == item.OutletId.Value) ||
                    r.RuleType == "global"
                ));

            // UPDATED: compute then write per-item result to Redis cache
            var priceResult = ComputePrice(variant, item.OutletId, opo, bestRule);
            results.Add(priceResult);
            await _posCache.SetPriceAsync(item.ProductVariantId, item.OutletId, item.Quantity, priceResult);
        }

        return results;
    }

    /// <summary>
    /// Single-variant resolution: loads the two data dependencies (outlet override + best rule)
    /// and delegates computation to the static <see cref="ComputePrice"/> helper.
    /// </summary>
    private async Task<PriceCalculationResultDto> ResolvePrice(
        ProductVariant variant,
        long? outletId,
        int quantity,
        DateTime asOfUtc)
    {
        OutletPriceOverride? opo = null;
        if (outletId.HasValue)
            opo = await _overrideRepo.GetActiveForVariantAsync(outletId.Value, variant.Id);

        // GetMatchingRulesAsync already limits to Take(1) so this FirstOrDefault is cheap
        var appliedRule = (await _ruleRepo.GetMatchingRulesAsync(
            variantId:  variant.Id,
            productId:  variant.ProductId,
            categoryId: variant.Product.CategoryId,
            outletId:   outletId,
            quantity:   quantity,
            asOfUtc:    asOfUtc)).FirstOrDefault();

        _logger.LogDebug(
            "Price: variant={VariantId} outlet={OutletId} override={Override} rule={Rule}",
            variant.Id, outletId, opo?.Id, appliedRule?.Name);

        return ComputePrice(variant, outletId, opo, appliedRule);
    }

    /// <summary>
    /// Pure price computation — no DB access. Shared by both single and batch paths.
    /// Steps:
    ///   1. base  = Product.BasePrice + Variant.PriceAdjustment
    ///   2. outlet = OutletPriceOverride if provided, else base
    ///   3. discount = best matching campaign rule (winner-takes-all on priority)
    ///   4. final = max(0, outlet − discount)
    ///   5. tax   = final × TaxRate / 100  (informational only — not baked into FinalPrice)
    /// </summary>
    private static PriceCalculationResultDto ComputePrice(
        ProductVariant variant,
        long? outletId,
        OutletPriceOverride? opo,
        PriceRule? appliedRule)
    {
        var product   = variant.Product;
        var breakdown = new List<PriceBreakdownLineDto>();

        // ── Step 1: Base price ─────────────────────────────────────────
        var basePrice = product.BasePrice + variant.PriceAdjustment;
        breakdown.Add(new() { Label = "Base price", Amount = basePrice });

        // ── Step 2: Outlet override ────────────────────────────────────
        var outletPrice     = basePrice;
        var overrideApplied = false;

        if (opo != null)
        {
            if (opo.OverrideType == "fixed")
            {
                outletPrice = opo.OverrideValue;
                breakdown.Add(new() { Label = "Outlet fixed price", Amount = outletPrice });
            }
            else // margin
            {
                var costBasis = product.CostPrice + variant.CostAdjustment;
                outletPrice = Math.Round(costBasis * (1m + opo.OverrideValue / 100m), 2);
                breakdown.Add(new() { Label = $"Outlet margin {opo.OverrideValue}% on cost", Amount = outletPrice });
            }
            overrideApplied = true;
        }

        // ── Step 3: Campaign / discount rule ──────────────────────────
        decimal discount = 0m;

        if (appliedRule != null)
        {
            discount = appliedRule.DiscountType == "percentage"
                ? Math.Round(outletPrice * appliedRule.DiscountValue / 100m, 2)
                : appliedRule.DiscountValue;

            // A fixed discount can never exceed the outlet price (no negative final prices)
            discount = Math.Min(discount, outletPrice);
            breakdown.Add(new() { Label = $"Discount: {appliedRule.Name}", Amount = -discount });
        }

        var finalPrice = Math.Round(outletPrice - discount, 2);

        // ── Step 4: Tax (informational) ────────────────────────────────
        var taxAmount = Math.Round(finalPrice * product.TaxRate / 100m, 2);
        breakdown.Add(new() { Label = $"Tax ({product.TaxRate}%)", Amount = taxAmount });

        return new PriceCalculationResultDto
        {
            ProductVariantId      = variant.Id,
            ProductName           = product.Name,
            VariantName           = variant.Name,
            BasePrice             = basePrice,
            OutletPrice           = outletPrice,
            DiscountAmount        = discount,
            FinalPrice            = finalPrice,
            TaxRate               = product.TaxRate,
            TaxAmount             = taxAmount,
            FinalPriceWithTax     = finalPrice + taxAmount,
            OutletOverrideApplied = overrideApplied,
            AppliedRuleId         = appliedRule?.Id,
            AppliedRuleName       = appliedRule?.Name,
            Breakdown             = breakdown
        };
    }
    // ──────────────────────────────────────────────────────────────────────
    //  Price Rules CRUD
    // ──────────────────────────────────────────────────────────────────────

    public async Task<PriceRuleListDto> SearchRulesAsync(PriceRuleSearchDto searchDto)
    {
        var (rules, total) = await _ruleRepo.SearchAsync(
            searchDto.SearchQuery,
            searchDto.RuleType,
            searchDto.IsActive,
            searchDto.PageNumber,
            searchDto.PageSize);

        return new PriceRuleListDto
        {
            Rules      = rules.Select(MapRuleToDto).ToList(),
            TotalCount = total,
            PageNumber  = searchDto.PageNumber,
            PageSize    = searchDto.PageSize
        };
    }

    public async Task<PriceRuleDto> GetRuleByIdAsync(long id)
    {
        var rule = await _ruleRepo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Price rule with ID {id} not found");

        return MapRuleToDto(rule);
    }

    public async Task<PriceRuleDto> CreateRuleAsync(CreatePriceRuleDto dto)
    {
        await ValidateRuleDtoAsync(dto);

        if (await _ruleRepo.NameExistsAsync(dto.Name))
            throw new InvalidOperationException($"A price rule named '{dto.Name}' already exists");

        var rule = new PriceRule
        {
            Name          = dto.Name.Trim(),
            Description   = dto.Description?.Trim(),
            RuleType      = dto.RuleType.ToLower(),
            TargetId      = dto.TargetId,
            DiscountType  = dto.DiscountType.ToLower(),
            DiscountValue = dto.DiscountValue,
            MinQuantity   = dto.MinQuantity,
            Priority      = dto.Priority,
            ValidFrom     = ToUtcOrNull(dto.ValidFrom),
            ValidTo       = ToUtcOrNull(dto.ValidTo),
            IsActive      = dto.IsActive
        };

        var created = await _ruleRepo.CreateAsync(rule);
        _logger.LogInformation("PriceRule created: {Name} (ID: {Id})", created.Name, created.Id);
        return MapRuleToDto(created);
    }

    public async Task<PriceRuleDto> UpdateRuleAsync(long id, UpdatePriceRuleDto dto)
    {
        var rule = await _ruleRepo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Price rule with ID {id} not found");

        await ValidateRuleDtoAsync(dto, excludeId: id);

        if (await _ruleRepo.NameExistsAsync(dto.Name, excludeId: id))
            throw new InvalidOperationException($"A price rule named '{dto.Name}' already exists");

        rule.Name          = dto.Name.Trim();
        rule.Description   = dto.Description?.Trim();
        rule.RuleType      = dto.RuleType.ToLower();
        rule.TargetId      = dto.TargetId;
        rule.DiscountType  = dto.DiscountType.ToLower();
        rule.DiscountValue = dto.DiscountValue;
        rule.MinQuantity   = dto.MinQuantity;
        rule.Priority      = dto.Priority;
        rule.ValidFrom     = ToUtcOrNull(dto.ValidFrom);
        rule.ValidTo       = ToUtcOrNull(dto.ValidTo);
        rule.IsActive      = dto.IsActive;

        var updated = await _ruleRepo.UpdateAsync(rule);
        _logger.LogInformation("PriceRule updated: {Name} (ID: {Id})", updated.Name, updated.Id);
        return MapRuleToDto(updated);
    }

    public async Task DeleteRuleAsync(long id)
    {
        _ = await _ruleRepo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Price rule with ID {id} not found");

        await _ruleRepo.DeleteAsync(id);
        _logger.LogInformation("PriceRule deleted: ID {Id}", id);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Outlet Price Overrides CRUD
    // ──────────────────────────────────────────────────────────────────────

    public async Task<OutletPriceOverrideListDto> GetOverridesByOutletAsync(long outletId)
    {
        var overrides = (await _overrideRepo.GetByOutletAsync(outletId)).ToList();
        return new OutletPriceOverrideListDto
        {
            Overrides  = overrides.Select(MapOverrideToDto).ToList(),
            TotalCount = overrides.Count
        };
    }

    public async Task<OutletPriceOverrideDto> GetOverrideByIdAsync(long id)
    {
        var opo = await _overrideRepo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Outlet price override with ID {id} not found");

        return MapOverrideToDto(opo);
    }

    public async Task<OutletPriceOverrideDto> CreateOverrideAsync(CreateOutletPriceOverrideDto dto)
    {
        if (!ValidOverrideTypes.Contains(dto.OverrideType))
            throw new InvalidOperationException(
                $"Invalid OverrideType '{dto.OverrideType}'. Must be 'fixed' or 'margin'");

        var outletExists = await _context.Outlets.AnyAsync(o => o.Id == dto.OutletId);
        if (!outletExists)
            throw new KeyNotFoundException($"Outlet with ID {dto.OutletId} not found");

        var variantExists = await _context.ProductVariants.AnyAsync(v => v.Id == dto.ProductVariantId);
        if (!variantExists)
            throw new KeyNotFoundException($"ProductVariant with ID {dto.ProductVariantId} not found");

        if (await _overrideRepo.ExistsAsync(dto.OutletId, dto.ProductVariantId))
            throw new InvalidOperationException(
                $"An override already exists for outlet {dto.OutletId} and variant {dto.ProductVariantId}. " +
                "Update the existing override instead.");

        var entity = new OutletPriceOverride
        {
            OutletId         = dto.OutletId,
            ProductVariantId = dto.ProductVariantId,
            OverrideType     = dto.OverrideType.ToLower(),
            OverrideValue    = dto.OverrideValue,
            IsActive         = dto.IsActive
        };

        await _overrideRepo.CreateAsync(entity);

        var created = await _overrideRepo.GetByIdAsync(entity.Id)
            ?? throw new InvalidOperationException("Failed to reload created outlet price override");

        _logger.LogInformation(
            "OutletPriceOverride created: outlet={OutletId} variant={VariantId} type={Type} value={Value}",
            dto.OutletId, dto.ProductVariantId, entity.OverrideType, entity.OverrideValue);

        return MapOverrideToDto(created);
    }

    public async Task<OutletPriceOverrideDto> UpdateOverrideAsync(long id, UpdateOutletPriceOverrideDto dto)
    {
        var opo = await _overrideRepo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Outlet price override with ID {id} not found");

        if (!ValidOverrideTypes.Contains(dto.OverrideType))
            throw new InvalidOperationException(
                $"Invalid OverrideType '{dto.OverrideType}'. Must be 'fixed' or 'margin'");

        opo.OverrideType  = dto.OverrideType.ToLower();
        opo.OverrideValue = dto.OverrideValue;
        opo.IsActive      = dto.IsActive;

        await _overrideRepo.UpdateAsync(opo);

        var updated = await _overrideRepo.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Failed to reload updated outlet price override");

        _logger.LogInformation("OutletPriceOverride {Id} updated", id);
        return MapOverrideToDto(updated);
    }

    public async Task DeleteOverrideAsync(long id)
    {
        _ = await _overrideRepo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Outlet price override with ID {id} not found");

        await _overrideRepo.DeleteAsync(id);
        _logger.LogInformation("OutletPriceOverride {Id} deleted", id);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────────────────────

    private async Task ValidateRuleDtoAsync(CreatePriceRuleDto dto, long? excludeId = null)
    {
        if (!ValidRuleTypes.Contains(dto.RuleType))
            throw new InvalidOperationException(
                $"Invalid RuleType '{dto.RuleType}'. Must be one of: {string.Join(", ", ValidRuleTypes)}");

        if (!ValidDiscountTypes.Contains(dto.DiscountType))
            throw new InvalidOperationException(
                $"Invalid DiscountType '{dto.DiscountType}'. Must be 'percentage' or 'fixed'");

        var ruleType = dto.RuleType.ToLower();

        if (ruleType != "global" && dto.TargetId == null)
            throw new InvalidOperationException(
                $"TargetId is required for RuleType '{dto.RuleType}'");

        if (dto.DiscountType.ToLower() == "percentage" && dto.DiscountValue > 100)
            throw new InvalidOperationException("Percentage discount cannot exceed 100%");

        if (dto.ValidFrom.HasValue && dto.ValidTo.HasValue && dto.ValidFrom > dto.ValidTo)
            throw new InvalidOperationException("ValidFrom must be earlier than or equal to ValidTo");

        // Validate that TargetId points to an existing entity
        if (dto.TargetId.HasValue)
        {
            var targetId = dto.TargetId.Value;
            var targetExists = ruleType switch
            {
                "variant"  => await _context.ProductVariants.AnyAsync(v => v.Id == targetId),
                "product"  => await _context.Products.AnyAsync(p => p.Id == targetId),
                "category" => await _context.Categories.AnyAsync(c => c.Id == targetId),
                "outlet"   => await _context.Outlets.AnyAsync(o => o.Id == targetId),
                _          => true
            };

            if (!targetExists)
                throw new KeyNotFoundException(
                    $"Target {dto.RuleType} with ID {targetId} not found");
        }
    }

    private static DateTime? ToUtcOrNull(DateTime? dt)
    {
        if (!dt.HasValue) return null;
        // Convert to UTC regardless of the original Kind so stored timestamps are always UTC.
        // Unspecified kind is treated as local time by ToUniversalTime().
        return dt.Value.Kind == DateTimeKind.Utc
            ? dt.Value
            : dt.Value.ToUniversalTime();
    }

    private static PriceRuleDto MapRuleToDto(PriceRule r) => new()
    {
        Id            = r.Id,
        Name          = r.Name,
        Description   = r.Description,
        RuleType      = r.RuleType,
        TargetId      = r.TargetId,
        DiscountType  = r.DiscountType,
        DiscountValue = r.DiscountValue,
        MinQuantity   = r.MinQuantity,
        Priority      = r.Priority,
        ValidFrom     = r.ValidFrom,
        ValidTo       = r.ValidTo,
        IsActive      = r.IsActive,
        CreatedAt     = r.CreatedAt
    };

    private static OutletPriceOverrideDto MapOverrideToDto(OutletPriceOverride o) => new()
    {
        Id               = o.Id,
        OutletId         = o.OutletId,
        OutletName       = o.Outlet?.Name ?? string.Empty,
        ProductVariantId = o.ProductVariantId,
        VariantName      = o.ProductVariant?.Name ?? string.Empty,
        ProductName      = o.ProductVariant?.Product?.Name ?? string.Empty,
        OverrideType     = o.OverrideType,
        OverrideValue    = o.OverrideValue,
        IsActive         = o.IsActive,
        CreatedAt        = o.CreatedAt
    };
}
