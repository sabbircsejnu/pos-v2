using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Pos;
using RetailPOS.API.DTOs.Pricing;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

// NEW ─────────────────────────────────────────────────────────────────────────
// Lightweight POS endpoints optimised for fast screen interactions.
//
// Design goals:
//   • Minimal payload  — small DTOs, no deep nav-chain serialisation
//   • Redis-first      — product + price results served from cache where possible
//   • Low DB calls     — ≤3 DB round-trips on a full cold cache miss
//   • POS-role clarity — endpoints named and scoped for POS terminal use
//
// Endpoint map:
//   GET  /api/pos/lookup      – barcode / SKU / variantId scan → product + price + stock hint
//   GET  /api/pos/stock-hint  – per-variant stock availability check (display, not gating)
//   POST /api/pos/cart-prices – batch price calculation for active cart lines
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// High-performance POS screen endpoints.  All results are Redis-cached by the
/// underlying services — see <see cref="IPosLookupService"/> and
/// <see cref="IPricingService"/> for TTL and invalidation details.
/// </summary>
[ApiController]
[Route("api/pos")]
[Authorize(Policy = "sales.create")]
public class PosController : ControllerBase
{
    private readonly IPosLookupService      _posLookup;
    private readonly IPricingService        _pricingService;
    private readonly ILogger<PosController> _logger;

    public PosController(
        IPosLookupService      posLookup,
        IPricingService        pricingService,
        ILogger<PosController> logger)
    {
        _posLookup      = posLookup;
        _pricingService = pricingService;
        _logger         = logger;
    }

    // ── GET /api/pos/lookup ───────────────────────────────────────────────────

    /// <summary>
    /// Primary POS barcode-scan endpoint.
    /// Resolves a barcode, SKU, or variantId to a full POS product card:
    /// product info + effective price for the outlet + stock display hint.
    ///
    /// Performance: Redis-cached.
    ///   Cache hit  → 0 DB calls, response ≲ 2 ms
    ///   Cache miss → ≤ 3 DB calls (variant+product join, pricing, inventory qty)
    ///
    /// Query parameters:
    ///   q        – the barcode, SKU, or numeric variantId to look up
    ///   outletId – optional outlet context; affects pricing and stock qty
    ///   mode     – "barcode" (default) | "sku" | "variant"
    /// </summary>
    [HttpGet("lookup")]
    [ProducesResponseType(typeof(ApiResponse<PosProductLookupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PosProductLookupDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PosProductLookupDto>>> Lookup(
        [FromQuery] string  q,
        [FromQuery] long?   outletId = null,
        [FromQuery] string  mode     = "barcode")
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(ApiResponse<PosProductLookupDto>.ErrorResponse("Query parameter 'q' is required."));

        PosProductLookupDto? result = mode.ToLowerInvariant() switch
        {
            "sku"     => await _posLookup.LookupBySkuAsync(q, outletId),
            "variant" => long.TryParse(q, out var vid)
                             ? await _posLookup.LookupByVariantIdAsync(vid, outletId)
                             : null,
            _         => await _posLookup.LookupByBarcodeAsync(q, outletId)  // default: barcode
        };

        if (result is null)
            return NotFound(ApiResponse<PosProductLookupDto>.ErrorResponse($"No active product found for '{q}'."));

        return Ok(ApiResponse<PosProductLookupDto>.SuccessResponse(result));
    }

    // ── GET /api/pos/stock-hint ───────────────────────────────────────────────

    /// <summary>
    /// Returns a lightweight stock availability hint used to colour POS cart rows
    /// (green = sufficient, amber = low, red = out of stock).
    ///
    /// The result may come from a 30-second Redis cache.
    ///
    /// ⚠ DISPLAY HINT ONLY — never use this as the gate for allowing a sale.
    ///   Definitive stock validation runs inside SaleService.CreateAsync within a
    ///   PostgreSQL transaction using xmin optimistic concurrency protection.
    ///
    /// Query parameters:
    ///   variantId – variant to check
    ///   outletId  – outlet to check stock at
    ///   qty       – requested quantity (default 1); used to populate Sufficient flag
    /// </summary>
    [HttpGet("stock-hint")]
    [ProducesResponseType(typeof(ApiResponse<PosStockHintDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PosStockHintDto>>> StockHint(
        [FromQuery] long variantId,
        [FromQuery] long outletId,
        [FromQuery] int  qty = 1)
    {
        var hint = await _posLookup.GetStockHintAsync(variantId, outletId, Math.Max(1, qty));
        return Ok(ApiResponse<PosStockHintDto>.SuccessResponse(hint));
    }

    // ── POST /api/pos/cart-prices ─────────────────────────────────────────────

    /// <summary>
    /// Batch price calculation for all lines currently in the POS cart.
    ///
    /// Preferred over calling POST /api/pricing/calculate-batch directly because:
    ///   1. Individual results are Redis-cached by PricingService — ≤1 DB query on hit
    ///   2. A single HTTP call replaces N per-item calls from the cart component
    ///
    /// Returns per-line effective price, discount breakdown, and tax amount.
    /// </summary>
    [HttpPost("cart-prices")]
    [ProducesResponseType(typeof(ApiResponse<List<PriceCalculationResultDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<PriceCalculationResultDto>>>> CartPrices(
        [FromBody] PriceCalculationBatchRequestDto dto)
    {
        var results = await _pricingService.CalculateBatchAsync(dto);
        return Ok(ApiResponse<List<PriceCalculationResultDto>>.SuccessResponse(results));
    }
}
