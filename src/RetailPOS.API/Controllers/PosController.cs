using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.DTOs.Pos;
using RetailPOS.API.DTOs.Pricing;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Pos.Queries;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Pos.Commands;

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
/// High-performance POS screen endpoints. Results use the configured POS
/// lookup/cache and pricing services for TTL and invalidation behavior.
/// </summary>
[ApiController]
[Route("api/pos")]
[Authorize(Policy = "sales.create")]
public class PosController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<PosController> _logger;
    private readonly IUserOutletAccessService _outletAccess;

    public PosController(
        ILogger<PosController> logger,
        IUserOutletAccessService outletAccess,
        ISender sender)
    {
        _logger         = logger;
        _outletAccess   = outletAccess;
        _sender = sender;
    }

    [HttpGet("cash-sessions/active")]
    [Authorize(Policy = "cash_sessions.view")]
    [ProducesResponseType(typeof(ApiResponse<CashSessionDto?>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CashSessionDto?>>> GetActiveCashSession(
        [FromQuery] long outletId,
        [FromQuery] long terminalId,
        CancellationToken cancellationToken)
    {
        var active = await _sender.Send(
            new GetActiveCashSessionQuery(outletId, terminalId),
            cancellationToken);
        if (active == null)
            return Ok(ApiResponse<CashSessionDto?>.SuccessResponse(null, "No active cash session."));

        return Ok(ApiResponse<CashSessionDto?>.SuccessResponse(MapCashSession(active)));
    }

    [HttpPost("cash-sessions/open")]
    [Authorize(Policy = "cash_sessions.open")]
    [ProducesResponseType(typeof(ApiResponse<CashSessionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CashSessionDto>>> OpenCashSession(
        [FromBody] OpenCashSessionDto dto,
        CancellationToken cancellationToken)
    {
        dto.OutletId = await _outletAccess.EnforceWriteOutletAsync(dto.OutletId);

        var openedByUserId = CashSessionApiMapper.ResolveUserId(User);
        if (openedByUserId is null)
            return Unauthorized(ApiResponse<CashSessionDto>.ErrorResponse("Unable to resolve user context."));

        var session = await _sender.Send(
            CashSessionApiMapper.ToCommand(dto, dto.OutletId, openedByUserId.Value),
            cancellationToken);
        return Ok(ApiResponse<CashSessionDto>.SuccessResponse(
            MapCashSession(session),
            "Cash session opened successfully."));
    }

    [HttpGet("cash-sessions/{id:long}/summary")]
    [Authorize(Policy = "cash_sessions.view")]
    [ProducesResponseType(typeof(ApiResponse<CashSessionCloseSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CashSessionCloseSummaryDto>>> GetCashSessionSummary(
        long id,
        CancellationToken cancellationToken)
    {
        var summary = await _sender.Send(new GetCashSessionCloseSummaryQuery(id), cancellationToken);
        return Ok(ApiResponse<CashSessionCloseSummaryDto>.SuccessResponse(CashSessionApiMapper.ToDto(summary)));
    }

    private static CashSessionDto MapCashSession(CashSessionReadModel session) =>
        CashSessionApiMapper.ToDto(session);

    [HttpPost("cash-sessions/{id:long}/close")]
    [Authorize(Policy = "cash_sessions.close")]
    [ProducesResponseType(typeof(ApiResponse<CashSessionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CashSessionDto>>> CloseCashSession(
        long id,
        [FromBody] CloseCashSessionDto dto,
        CancellationToken cancellationToken)
    {
        var closedByUserId = CashSessionApiMapper.ResolveUserId(User);
        if (closedByUserId is null)
            return Unauthorized(ApiResponse<CashSessionDto>.ErrorResponse("Unable to resolve user context."));

        var outletId = await _sender.Send(new GetCashSessionOutletIdQuery(id), cancellationToken);
        await _outletAccess.ResolveAndAuthorizeLocationAsync(outletId, "outlet");
        var session = await _sender.Send(
            CashSessionApiMapper.ToCommand(id, dto, closedByUserId.Value),
            cancellationToken);
        return Ok(ApiResponse<CashSessionDto>.SuccessResponse(
            MapCashSession(session),
            "Cash session closed successfully."));
    }

    /// <summary>
    /// Phase 4 salesperson selector: active users of the Business assigned to the outlet (primary-outlet users first)
    /// and the default (the terminal's session opener when eligible, otherwise the caller when eligible). The same rule is
    /// enforced again when the sale is created.
    /// </summary>
    [HttpGet("salespersons")]
    [ProducesResponseType(typeof(ApiResponse<RetailPOS.Application.Sales.Queries.SalespersonOptionsReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RetailPOS.Application.Sales.Queries.SalespersonOptionsReadModel>>> GetSalespersons(
        [FromQuery] long? outletId,
        [FromQuery] long? terminalId,
        CancellationToken cancellationToken = default)
    {
        var userId = CashSessionApiMapper.ResolveUserId(User);
        if (userId is null)
            return Unauthorized(ApiResponse<RetailPOS.Application.Sales.Queries.SalespersonOptionsReadModel>.ErrorResponse("Unable to resolve user context."));

        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
        if (!resolvedOutletId.HasValue)
            return BadRequest(ApiResponse<RetailPOS.Application.Sales.Queries.SalespersonOptionsReadModel>.ErrorResponse("OutletId is required to load salespersons."));

        var options = await _sender.Send(
            new RetailPOS.Application.Sales.Queries.GetEligibleSalespersonsQuery(resolvedOutletId.Value, terminalId, userId.Value),
            cancellationToken);
        return Ok(ApiResponse<RetailPOS.Application.Sales.Queries.SalespersonOptionsReadModel>.SuccessResponse(options));
    }

    [HttpGet("terminals")]
    [ProducesResponseType(typeof(ApiResponse<List<PosTerminalDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<PosTerminalDto>>>> GetTerminals(
        [FromQuery] long? outletId = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
        if (!resolvedOutletId.HasValue)
            return BadRequest(ApiResponse<List<PosTerminalDto>>.ErrorResponse("OutletId is required to load POS terminals."));

        var terminals = await _sender.Send(
            new GetPosTerminalsQuery(resolvedOutletId.Value),
            cancellationToken);
        return Ok(ApiResponse<List<PosTerminalDto>>.SuccessResponse(
            terminals.Select(terminal => new PosTerminalDto
            {
                Id = terminal.Id,
                Name = terminal.Name,
                Code = terminal.Code,
                IsDefault = terminal.IsDefault
            }).ToList()));
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
    [ProducesResponseType(typeof(ApiResponse<PosProductLookupDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PosProductLookupDto>>> Lookup(
        [FromQuery] string  q,
        [FromQuery] long?   outletId = null,
        [FromQuery] string  mode     = "barcode",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(ApiResponse<PosProductLookupDto>.ErrorResponse("Query parameter 'q' is required."));

        if (outletId.HasValue)
        {
            try
            {
                outletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    ApiResponse<PosProductLookupDto>.ErrorResponse(ex.Message));
            }
        }

        var lookupMode = mode.ToLowerInvariant() switch
        {
            "sku" => PosProductLookupMode.Sku,
            "variant" => PosProductLookupMode.Variant,
            _ => PosProductLookupMode.Barcode
        };

        var readModel = await _sender.Send(
            new GetPosProductLookupQuery(q, lookupMode, outletId),
            cancellationToken);
        var result = readModel is null ? null : ToLookupDto(readModel);

        if (result is null)
            return NotFound(ApiResponse<PosProductLookupDto>.ErrorResponse($"No active product found for '{q}'."));

        return Ok(ApiResponse<PosProductLookupDto>.SuccessResponse(result));
    }

    private static PosProductLookupDto ToLookupDto(PosProductLookupReadModel result) => new()
    {
        VariantId = result.VariantId,
        ProductId = result.ProductId,
        ProductName = result.ProductName,
        VariantName = result.VariantName,
        Barcode = result.Barcode,
        Sku = result.Sku,
        ImageUrl = result.ImageUrl,
        BasePrice = result.BasePrice,
        EffectivePrice = result.EffectivePrice,
        TaxRate = result.TaxRate,
        TaxClassification = result.TaxClassification,
        PricesIncludeTax = result.PricesIncludeTax,
        AppliedRuleName = result.AppliedRuleName,
        StockQty = result.StockQty,
        InStock = result.InStock,
        StockFromCache = result.StockFromCache
    };

    // ── GET /api/pos/stock-hint ───────────────────────────────────────────────

    /// <summary>
    /// Returns a lightweight stock availability hint used to colour POS cart rows
    /// (green = sufficient, amber = low, red = out of stock).
    ///
    /// The result may come from a 30-second Redis cache.
    ///
    /// ⚠ DISPLAY HINT ONLY — never use this as the gate for allowing a sale.
    ///   Definitive stock validation runs inside the Application sale command within a
    ///   PostgreSQL transaction using xmin optimistic concurrency protection.
    ///
    /// Query parameters:
    ///   variantId – variant to check
    ///   outletId  – outlet to check stock at
    ///   qty       – requested quantity (default 1); used to populate Sufficient flag
    /// </summary>
    [HttpGet("stock-hint")]
    [ProducesResponseType(typeof(ApiResponse<PosStockHintDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PosStockHintDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PosStockHintDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PosStockHintDto>>> StockHint(
        [FromQuery] long variantId,
        [FromQuery] long outletId,
        [FromQuery] int  qty = 1,
        CancellationToken cancellationToken = default)
    {
        if (outletId <= 0)
        {
            return BadRequest(
                ApiResponse<PosStockHintDto>.ErrorResponse("A positive outletId is required."));
        }

        try
        {
            await _outletAccess.ResolveAndAuthorizeLocationAsync(outletId, "outlet");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<PosStockHintDto>.ErrorResponse(ex.Message));
        }

        var result = await _sender.Send(
            new GetPosStockHintQuery(variantId, outletId, Math.Max(1, qty)),
            cancellationToken);
        var hint = new PosStockHintDto
        {
            VariantId = result.VariantId,
            OutletId = result.OutletId,
            AvailableQty = result.AvailableQty,
            Sufficient = result.Sufficient,
            FromCache = result.FromCache
        };
        return Ok(ApiResponse<PosStockHintDto>.SuccessResponse(hint));
    }

    // ── POST /api/pos/cart-prices ─────────────────────────────────────────────

    /// <summary>
    /// Batch price calculation for all lines currently in the POS cart.
    ///
    /// Preferred over calling POST /api/pricing/calculate-batch directly because:
    ///   1. Individual results are Redis-cached by the Application POS pricing query — ≤1 DB query on hit
    ///   2. A single HTTP call replaces N per-item calls from the cart component
    ///
    /// Returns per-line effective price, discount breakdown, and tax amount.
    /// </summary>
    [HttpPost("cart-prices")]
    [ProducesResponseType(typeof(ApiResponse<List<PriceCalculationResultDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<PriceCalculationResultDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<List<PriceCalculationResultDto>>>> CartPrices(
        [FromBody] PriceCalculationBatchRequestDto dto,
        CancellationToken cancellationToken)
    {
        foreach (var item in dto.Items)
        {
            if (!item.OutletId.HasValue)
            {
                continue;
            }

            try
            {
                item.OutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(item.OutletId);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    ApiResponse<List<PriceCalculationResultDto>>.ErrorResponse(ex.Message));
            }
        }

        var results = await _sender.Send(
            new CalculatePosCartPricesQuery(dto.Items
                .Select(item => new PosCartPriceLine(
                    item.ProductVariantId,
                    item.OutletId,
                    item.Quantity))
                .ToArray()),
            cancellationToken);
        return Ok(ApiResponse<List<PriceCalculationResultDto>>.SuccessResponse(
            results.Select(ToPriceDto).ToList()));
    }

    private static PriceCalculationResultDto ToPriceDto(PosCartPriceReadModel result) => new()
    {
        ProductVariantId = result.ProductVariantId,
        ProductName = result.ProductName,
        VariantName = result.VariantName,
        BasePrice = result.BasePrice,
        OutletPrice = result.OutletPrice,
        DiscountAmount = result.DiscountAmount,
        FinalPrice = result.FinalPrice,
        TaxRate = result.TaxRate,
        TaxClassification = result.TaxClassification,
        PricesIncludeTax = result.PricesIncludeTax,
        TaxAmount = result.TaxAmount,
        FinalPriceWithTax = result.FinalPriceWithTax,
        OutletOverrideApplied = result.OutletOverrideApplied,
        AppliedRuleId = result.AppliedRuleId,
        AppliedRuleName = result.AppliedRuleName,
        Breakdown = result.Breakdown.Select(line => new PriceBreakdownLineDto
        {
            Label = line.Label,
            Amount = line.Amount
        }).ToList()
    };
}
