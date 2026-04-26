using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using RetailPOS.API.DTOs.Pos;
using RetailPOS.API.DTOs.Pricing;

namespace RetailPOS.API.Services;

// NEW ─────────────────────────────────────────────────────────────────────────
// IDistributedCache-backed POS cache.
// Works with both Redis (production) and the in-memory distributed cache
// (development / CI when no Redis connection string is configured).
// All cache failures are swallowed as warnings — a Redis outage must never
// block a sale.
// ─────────────────────────────────────────────────────────────────────────────

/// <inheritdoc cref="IPosCacheService"/>
public sealed class PosCacheService : IPosCacheService
{
    private readonly IDistributedCache          _cache;
    private readonly ILogger<PosCacheService>   _logger;

    // TTLs — short enough to keep stock display useful, long enough to cut DB load
    private static readonly TimeSpan LookupTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PriceTtl  = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StockTtl  = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PosCacheService(IDistributedCache cache, ILogger<PosCacheService> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    // ── Product lookup ────────────────────────────────────────────────────────

    public async Task<PosProductLookupDto?> GetLookupAsync(string cacheKey)
    {
        try
        {
            var raw = await _cache.GetStringAsync(cacheKey);
            return raw is null ? null : JsonSerializer.Deserialize<PosProductLookupDto>(raw, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POS cache GET failed for key {Key}", cacheKey);
            return null;
        }
    }

    public async Task SetLookupAsync(string cacheKey, PosProductLookupDto dto)
    {
        try
        {
            var json = JsonSerializer.Serialize(dto, JsonOpts);
            await _cache.SetStringAsync(cacheKey, json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = LookupTtl });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POS cache SET failed for key {Key}", cacheKey);
        }
    }

    public async Task InvalidateLookupAsync(long variantId, string? barcode, string? sku, long? outletId = null)
    {
        var keys = new List<string> { LookupVariantKey(variantId, outletId) };

        // Remove the zero-outlet baseline entries too
        if (outletId.HasValue)
            keys.Add(LookupVariantKey(variantId, null));

        if (barcode is not null)
        {
            keys.Add(LookupBarcodeKey(barcode, outletId));
            if (outletId.HasValue)
                keys.Add(LookupBarcodeKey(barcode, null));
        }
        if (sku is not null)
        {
            keys.Add(LookupSkuKey(sku, outletId));
            if (outletId.HasValue)
                keys.Add(LookupSkuKey(sku, null));
        }

        foreach (var key in keys)
        {
            try   { await _cache.RemoveAsync(key); }
            catch (Exception ex) { _logger.LogWarning(ex, "POS cache REMOVE failed for key {Key}", key); }
        }
    }

    // ── Price result ──────────────────────────────────────────────────────────

    public async Task<PriceCalculationResultDto?> GetPriceAsync(long variantId, long? outletId, int qty)
    {
        try
        {
            var raw = await _cache.GetStringAsync(PriceKey(variantId, outletId, qty));
            return raw is null ? null : JsonSerializer.Deserialize<PriceCalculationResultDto>(raw, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POS price cache GET failed for variant {VariantId}", variantId);
            return null;
        }
    }

    public async Task SetPriceAsync(long variantId, long? outletId, int qty, PriceCalculationResultDto dto)
    {
        try
        {
            var json = JsonSerializer.Serialize(dto, JsonOpts);
            await _cache.SetStringAsync(PriceKey(variantId, outletId, qty), json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = PriceTtl });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POS price cache SET failed for variant {VariantId}", variantId);
        }
    }

    public async Task InvalidatePriceAsync(long variantId, long? outletId)
    {
        // Removes the most common qty=1 entry. Other qty flavours expire via TTL.
        var key = PriceKey(variantId, outletId, 1);
        try   { await _cache.RemoveAsync(key); }
        catch (Exception ex) { _logger.LogWarning(ex, "POS price cache REMOVE failed for key {Key}", key); }
    }

    // ── Stock hint ────────────────────────────────────────────────────────────

    public async Task<int?> GetStockHintAsync(long variantId, long outletId)
    {
        try
        {
            var raw = await _cache.GetStringAsync(StockKey(variantId, outletId));
            return raw is null ? null : int.Parse(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POS stock cache GET failed for variant {VariantId}", variantId);
            return null;
        }
    }

    public async Task SetStockHintAsync(long variantId, long outletId, int qty)
    {
        try
        {
            await _cache.SetStringAsync(StockKey(variantId, outletId), qty.ToString(),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = StockTtl });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POS stock cache SET failed for variant {VariantId}", variantId);
        }
    }

    public async Task InvalidateStockAsync(long variantId, long outletId)
    {
        try   { await _cache.RemoveAsync(StockKey(variantId, outletId)); }
        catch (Exception ex) { _logger.LogWarning(ex, "POS stock cache REMOVE failed for variant {VariantId}", variantId); }
    }

    // ── Static key builders (internal so PosLookupService can reuse them) ─────

    internal static string LookupVariantKey(long variantId, long? outletId) =>
        $"pos:lookup:variant:{variantId}:{outletId ?? 0}";

    internal static string LookupBarcodeKey(string barcode, long? outletId) =>
        $"pos:lookup:barcode:{barcode}:{outletId ?? 0}";

    internal static string LookupSkuKey(string sku, long? outletId) =>
        $"pos:lookup:sku:{sku}:{outletId ?? 0}";

    internal static string PriceKey(long variantId, long? outletId, int qty) =>
        $"pos:price:{variantId}:{outletId ?? 0}:{qty}";

    internal static string StockKey(long variantId, long outletId) =>
        $"pos:stock:{variantId}:{outletId}";
}
