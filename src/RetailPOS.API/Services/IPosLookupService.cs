using RetailPOS.API.DTOs.Pos;

namespace RetailPOS.API.Services;

// NEW ─────────────────────────────────────────────────────────────────────────
// Fast POS product / price / stock lookup abstraction.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Provides fast, Redis-cached product lookups for the POS screen.
///
/// DB access per lookup:
///   Cache hit  → 0 DB calls
///   Cache miss → up to 3 calls (variant+product join, pricing, inventory qty)
///
/// The result combines product info, effective price, and a stock display hint
/// in a single request — replacing three separate round-trips from the frontend.
/// </summary>
public interface IPosLookupService
{
    /// <summary>
    /// Resolve by barcode.
    /// Checks variant.Barcode first, then falls back to product.Barcode
    /// (for simple products that store the barcode at product level).
    /// </summary>
    Task<PosProductLookupDto?> LookupByBarcodeAsync(string barcode, long? outletId);

    /// <summary>Resolve by variant SKU.</summary>
    Task<PosProductLookupDto?> LookupBySkuAsync(string sku, long? outletId);

    /// <summary>Resolve by known variantId (used when the cart already has the ID).</summary>
    Task<PosProductLookupDto?> LookupByVariantIdAsync(long variantId, long? outletId);

    /// <summary>
    /// Returns a lightweight stock hint for cart-row colouring (green / amber / red).
    ///
    /// ⚠ This is a DISPLAY HINT only.
    /// Final stock validation always runs inside SaleService.CreateAsync within a
    /// DB transaction protected by xmin optimistic concurrency — not here.
    /// </summary>
    Task<PosStockHintDto> GetStockHintAsync(long variantId, long outletId, int requestedQty);
}
