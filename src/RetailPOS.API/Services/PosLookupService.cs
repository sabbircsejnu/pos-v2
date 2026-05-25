using Microsoft.EntityFrameworkCore;
using RetailPOS.API.DTOs.Pos;
using RetailPOS.API.DTOs.Pricing;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.API.Services;

// NEW ─────────────────────────────────────────────────────────────────────────
// Fast POS lookup service — combines product info, effective price, and stock
// hint in the smallest number of DB round-trips possible.
//
// Hot path (cache hit):
//   GET /api/pos/lookup?q=123456&outletId=1
//   → cache hit → 0 DB calls, ~1 ms response
//
// Cold path (first scan / post-invalidation):
//   1. SELECT on product_variants JOIN products  (lean projection, no nav chains)
//   2. Effective price via PricingService        (itself Redis-cached after first call)
//   3. SELECT quantity FROM inventories          (single int, keyed by variantId+outletId)
//   → Result is written to Redis before returning
//   Total = 3 DB calls max, then 0 for the next LookupTtl window
// ─────────────────────────────────────────────────────────────────────────────

/// <inheritdoc cref="IPosLookupService"/>
public sealed class PosLookupService : IPosLookupService
{
    private readonly RetailPOSDbContext         _context;
    private readonly IPricingService            _pricingService;
    private readonly IPosCacheService           _cache;
    private readonly ILogger<PosLookupService>  _logger;

    public PosLookupService(
        RetailPOSDbContext        context,
        IPricingService           pricingService,
        IPosCacheService          cache,
        ILogger<PosLookupService> logger)
    {
        _context        = context;
        _pricingService = pricingService;
        _cache          = cache;
        _logger         = logger;
    }

    // ── Public lookup methods ─────────────────────────────────────────────────

    public async Task<PosProductLookupDto?> LookupByBarcodeAsync(string barcode, long? outletId)
    {
        var cacheKey = PosCacheService.LookupBarcodeKey(barcode, outletId);
        var cached   = await _cache.GetLookupAsync(cacheKey);
        if (cached is not null) return cached;

        var dto = await FetchByBarcodeAsync(barcode, outletId);
        if (dto is not null) await _cache.SetLookupAsync(cacheKey, dto);
        return dto;
    }

    public async Task<PosProductLookupDto?> LookupBySkuAsync(string sku, long? outletId)
    {
        var cacheKey = PosCacheService.LookupSkuKey(sku, outletId);
        var cached   = await _cache.GetLookupAsync(cacheKey);
        if (cached is not null) return cached;

        var dto = await FetchBySkuAsync(sku, outletId);
        if (dto is not null) await _cache.SetLookupAsync(cacheKey, dto);
        return dto;
    }

    public async Task<PosProductLookupDto?> LookupByVariantIdAsync(long variantId, long? outletId)
    {
        var cacheKey = PosCacheService.LookupVariantKey(variantId, outletId);
        var cached   = await _cache.GetLookupAsync(cacheKey);
        if (cached is not null) return cached;

        var dto = await FetchByVariantIdAsync(variantId, outletId);
        if (dto is not null) await _cache.SetLookupAsync(cacheKey, dto);
        return dto;
    }

    public async Task<PosStockHintDto> GetStockHintAsync(long variantId, long outletId, int requestedQty)
    {
        var cachedQty = await _cache.GetStockHintAsync(variantId, outletId);
        int  qty;
        bool fromCache;

        if (cachedQty.HasValue)
        {
            qty       = cachedQty.Value;
            fromCache = true;
        }
        else
        {
            qty = await _context.Inventories
                .Where(i => i.VariantId    == variantId &&
                            i.LocationId   == outletId  &&
                            i.LocationType == "outlet")
                .Select(i => i.Quantity)
                .FirstOrDefaultAsync();

            await _cache.SetStockHintAsync(variantId, outletId, qty);
            fromCache = false;
        }

        return new PosStockHintDto
        {
            VariantId    = variantId,
            OutletId     = outletId,
            AvailableQty = qty,
            Sufficient   = qty >= requestedQty,
            FromCache    = fromCache
        };
    }

    // ── Private DB fetch helpers ──────────────────────────────────────────────
    // All selects use lean projections: no deep Include chains, no navigation
    // properties — only the columns the POS screen actually needs.

    private async Task<PosProductLookupDto?> FetchByBarcodeAsync(string barcode, long? outletId)
    {
        // 1. Try variant-level barcode (most common for products with named variants)
        var proj = await _context.ProductVariants
            .Where(v => v.Barcode == barcode)
            .Select(v => new VariantProjection
            {
                VariantId   = v.Id,
                ProductId   = v.ProductId,
                ProductName = v.Product.Name,
                VariantName = v.Name,
                Barcode     = v.Barcode,
                Sku         = v.Sku,
                ImageUrl    = v.Product.Images.Where(i => i.IsPrimary).Select(i => i.ThumbPath).FirstOrDefault(),
                BasePrice   = v.Product.BasePrice + v.PriceAdjustment,
                TaxRate     = v.Product.TaxRate,
                IsActive    = v.Product.IsActive
            })
            .FirstOrDefaultAsync();

        // 2. Fall back to product-level barcode (simple products with a single default variant)
        if (proj is null)
        {
            proj = await _context.Products
                .Where(p => p.Barcode == barcode && p.IsActive)
                .SelectMany(
                    p => p.ProductVariants.Take(1),
                    (p, v) => new VariantProjection
                    {
                        VariantId   = v.Id,
                        ProductId   = p.Id,
                        ProductName = p.Name,
                        VariantName = v.Name,
                        Barcode     = p.Barcode,
                        Sku         = v.Sku,
                        ImageUrl    = p.Images.Where(i => i.IsPrimary).Select(i => i.ThumbPath).FirstOrDefault(),
                        BasePrice   = p.BasePrice + v.PriceAdjustment,
                        TaxRate     = p.TaxRate,
                        IsActive    = p.IsActive
                    })
                .FirstOrDefaultAsync();
        }

        if (proj is null || !proj.IsActive) return null;
        return await BuildDtoAsync(proj, outletId);
    }

    private async Task<PosProductLookupDto?> FetchBySkuAsync(string sku, long? outletId)
    {
        var proj = await _context.ProductVariants
            .Where(v => v.Sku == sku)
            .Select(v => new VariantProjection
            {
                VariantId   = v.Id,
                ProductId   = v.ProductId,
                ProductName = v.Product.Name,
                VariantName = v.Name,
                Barcode     = v.Barcode,
                Sku         = v.Sku,
                ImageUrl    = v.Product.Images.Where(i => i.IsPrimary).Select(i => i.ThumbPath).FirstOrDefault(),
                BasePrice   = v.Product.BasePrice + v.PriceAdjustment,
                TaxRate     = v.Product.TaxRate,
                IsActive    = v.Product.IsActive
            })
            .FirstOrDefaultAsync();

        if (proj is null || !proj.IsActive) return null;
        return await BuildDtoAsync(proj, outletId);
    }

    private async Task<PosProductLookupDto?> FetchByVariantIdAsync(long variantId, long? outletId)
    {
        var proj = await _context.ProductVariants
            .Where(v => v.Id == variantId)
            .Select(v => new VariantProjection
            {
                VariantId   = v.Id,
                ProductId   = v.ProductId,
                ProductName = v.Product.Name,
                VariantName = v.Name,
                Barcode     = v.Barcode,
                Sku         = v.Sku,
                ImageUrl    = v.Product.Images.Where(i => i.IsPrimary).Select(i => i.ThumbPath).FirstOrDefault(),
                BasePrice   = v.Product.BasePrice + v.PriceAdjustment,
                TaxRate     = v.Product.TaxRate,
                IsActive    = v.Product.IsActive
            })
            .FirstOrDefaultAsync();

        if (proj is null || !proj.IsActive) return null;
        return await BuildDtoAsync(proj, outletId);
    }

    /// <summary>
    /// Assembles the final DTO from a lean projection.
    /// Makes at most 2 additional calls: one to PricingService (cached),
    /// one to Inventories if the stock hint is not already in Redis.
    /// </summary>
    private async Task<PosProductLookupDto> BuildDtoAsync(VariantProjection v, long? outletId)
    {
        // Effective price — PricingService writes its own cache entry on first call
        var price = await _pricingService.CalculateAsync(new PriceCalculationRequestDto
        {
            ProductVariantId = v.VariantId,
            OutletId         = outletId,
            Quantity         = 1
        });

        // Stock hint — read from Redis or a single int SELECT
        int  stockQty  = 0;
        bool fromCache = false;

        if (outletId.HasValue)
        {
            var cached = await _cache.GetStockHintAsync(v.VariantId, outletId.Value);
            if (cached.HasValue)
            {
                stockQty  = cached.Value;
                fromCache = true;
            }
            else
            {
                stockQty = await _context.Inventories
                    .Where(i => i.VariantId    == v.VariantId    &&
                                i.LocationId   == outletId.Value &&
                                i.LocationType == "outlet")
                    .Select(i => i.Quantity)
                    .FirstOrDefaultAsync();

                await _cache.SetStockHintAsync(v.VariantId, outletId.Value, stockQty);
            }
        }

        return new PosProductLookupDto
        {
            VariantId       = v.VariantId,
            ProductId       = v.ProductId,
            ProductName     = v.ProductName,
            VariantName     = v.VariantName,
            Barcode         = v.Barcode,
            Sku             = v.Sku,
            ImageUrl        = v.ImageUrl,
            BasePrice       = v.BasePrice,
            EffectivePrice  = price.FinalPrice,
            TaxRate         = v.TaxRate,
            AppliedRuleName = price.AppliedRuleName,
            StockQty        = stockQty,
            InStock         = stockQty > 0,
            StockFromCache  = fromCache
        };
    }

    // ── Internal projection record ────────────────────────────────────────────

    private sealed class VariantProjection
    {
        public long    VariantId   { get; init; }
        public long    ProductId   { get; init; }
        public string  ProductName { get; init; } = string.Empty;
        public string  VariantName { get; init; } = string.Empty;
        public string? Barcode     { get; init; }
        public string  Sku         { get; init; } = string.Empty;
        public string? ImageUrl    { get; init; }
        public decimal BasePrice   { get; init; }
        public decimal TaxRate     { get; init; }
        public bool    IsActive    { get; init; }
    }
}
