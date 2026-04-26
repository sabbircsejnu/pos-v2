using RetailPOS.API.DTOs.Pos;
using RetailPOS.API.DTOs.Pricing;

namespace RetailPOS.API.Services;

// NEW ─────────────────────────────────────────────────────────────────────────
// POS Redis cache abstraction.
//
// Cache TTLs (defaults; overridable via PosCache config section):
//   Product lookup  – 10 minutes  (invalidated on product/variant mutation)
//   Price result    –  5 minutes  (invalidated on price-rule / override mutation)
//   Stock hint      – 30 seconds  (invalidated after every stock-changing transaction)
//
// What should NEVER be trusted from cache alone:
//   • Final stock availability at checkout  → must read live DB row + xmin token
//   • Payment and totals                    → always computed server-side at sale time
//
// Cache key patterns:
//   pos:lookup:variant:{variantId}:{outletId}
//   pos:lookup:barcode:{barcode}:{outletId}
//   pos:lookup:sku:{sku}:{outletId}
//   pos:price:{variantId}:{outletId}:{qty}
//   pos:stock:{variantId}:{outletId}
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Abstracts all Redis / IDistributedCache operations used by the POS performance layer.
/// Failures are logged as warnings and fall through silently so a Redis outage never
/// breaks the sale flow — it just causes extra DB reads.
/// </summary>
public interface IPosCacheService
{
    // ── Product lookup ────────────────────────────────────────────────────────
    Task<PosProductLookupDto?> GetLookupAsync(string cacheKey);
    Task SetLookupAsync(string cacheKey, PosProductLookupDto dto);
    /// <summary>
    /// Removes all lookup cache entries associated with this variant.
    /// Called whenever a product, variant, price rule, or outlet override is mutated.
    /// Barcode/SKU-keyed entries for other outlet combinations expire via TTL.
    /// </summary>
    Task InvalidateLookupAsync(long variantId, string? barcode, string? sku, long? outletId = null);

    // ── Price result ──────────────────────────────────────────────────────────
    Task<PriceCalculationResultDto?> GetPriceAsync(long variantId, long? outletId, int qty);
    Task SetPriceAsync(long variantId, long? outletId, int qty, PriceCalculationResultDto dto);
    /// <summary>Removes price cache for this variant × outlet combination.</summary>
    Task InvalidatePriceAsync(long variantId, long? outletId);

    // ── Stock hint (display only — 30-second TTL) ─────────────────────────────
    Task<int?> GetStockHintAsync(long variantId, long outletId);
    Task SetStockHintAsync(long variantId, long outletId, int qty);
    /// <summary>
    /// Called immediately after any transaction that changes inventory
    /// (sale, void, GRN, stock adjustment, stock transfer)
    /// so the next barcode scan gets a fresh quantity.
    /// </summary>
    Task InvalidateStockAsync(long variantId, long outletId);
}
