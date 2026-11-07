using Microsoft.Extensions.Caching.Distributed;

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

    public PosCacheService(IDistributedCache cache, ILogger<PosCacheService> logger)
    {
        _cache  = cache;
        _logger = logger;
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

    public async Task InvalidatePriceAsync(long variantId, long? outletId)
    {
        // Removes the most common qty=1 entry. Other qty flavours expire via TTL.
        var key = PriceKey(variantId, outletId, 1);
        try   { await _cache.RemoveAsync(key); }
        catch (Exception ex) { _logger.LogWarning(ex, "POS price cache REMOVE failed for key {Key}", key); }
    }

    public async Task InvalidateStockAsync(long variantId, long outletId)
    {
        try   { await _cache.RemoveAsync(StockKey(variantId, outletId)); }
        catch (Exception ex) { _logger.LogWarning(ex, "POS stock cache REMOVE failed for variant {VariantId}", variantId); }
    }

    // ── Static key builders used by lookup invalidation ───────────────────────

    internal static string LookupVariantKey(long variantId, long? outletId) =>
        $"pos:lookup:variant:{variantId}:{outletId ?? 0}";

    internal static string LookupBarcodeKey(string barcode, long? outletId) =>
        $"pos:lookup:barcode:{barcode}:{outletId ?? 0}";

    internal static string LookupSkuKey(string sku, long? outletId) =>
        $"pos:lookup:sku:{sku}:{outletId ?? 0}";

    internal static string PriceKey(long variantId, long? outletId, decimal qty) =>
        $"pos:price:{variantId}:{outletId ?? 0}:{qty}";

    internal static string StockKey(long variantId, long outletId) =>
        $"pos:stock:{variantId}:{outletId}";
}
