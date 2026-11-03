namespace RetailPOS.API.Services;

// NEW ─────────────────────────────────────────────────────────────────────────
// POS Redis cache abstraction.
//
// Cache TTLs (defaults; overridable via PosCache config section):
//   Product lookup  – 10 minutes  (orphaned by the pricing cache version on product/variant mutation)
//   Price result    –  5 minutes  (orphaned by the pricing cache version on price-rule / override mutation)
//   Stock hint      – 30 seconds  (invalidated after every stock-changing transaction)
//
// Price and lookup entries are keyed by Business, tax policy and a per-Business/Outlet version token
// (see IPosPricingCacheVersion); the InvalidateLookup/InvalidatePrice methods below predate that scheme, do not
// address those keys and are retained only for the stock path and legacy callers.
//
// What should NEVER be trusted from cache alone:
//   • Final stock availability at checkout  → must read live DB row + xmin token
//   • Payment and totals                    → always computed server-side at sale time
//
// Legacy key patterns:
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
    /// <summary>
    /// Removes all lookup cache entries associated with this variant.
    /// Called whenever a product, variant, price rule, or outlet override is mutated.
    /// Barcode/SKU-keyed entries for other outlet combinations expire via TTL.
    /// </summary>
    Task InvalidateLookupAsync(long variantId, string? barcode, string? sku, long? outletId = null);

    /// <summary>Removes price cache for this variant × outlet combination.</summary>
    Task InvalidatePriceAsync(long variantId, long? outletId);

    /// <summary>
    /// Called immediately after any transaction that changes inventory
    /// (sale, void, GRN, stock adjustment, stock transfer)
    /// so the next barcode scan gets a fresh quantity.
    /// </summary>
    Task InvalidateStockAsync(long variantId, long outletId);
}
