// NEW — Hold / Park Sale controller
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

/// <summary>
/// Endpoints for parking (holding) and recalling a POS cart without committing a sale.
///
/// Flow:
///   1. Cashier builds a cart but needs to serve another customer → POST /api/held-sales
///   2. Cart is saved server-side; no inventory is reserved.
///   3. Cashier lists parked sales → GET /api/held-sales/outlet/{outletId}
///   4. Cashier resumes → GET /api/held-sales/{id}  (gets the cart back)
///   5. Cashier submits the cart normally via POST /api/sales
///   6. Cashier deletes the held record → DELETE /api/held-sales/{id}
/// </summary>
[ApiController]
[Route("api/held-sales")]
[Authorize(Policy = "sales.view")]
public class HeldSalesController : ControllerBase
{
    private readonly IHeldSaleService _heldSaleService;
    private readonly IFeatureEntitlementService _featureEntitlement;

    public HeldSalesController(IHeldSaleService heldSaleService, IFeatureEntitlementService featureEntitlement)
    {
        _heldSaleService = heldSaleService;
        _featureEntitlement = featureEntitlement;
    }

    // ── POST /api/held-sales ──────────────────────────────────────────────────

    /// <summary>Park/hold the current cart.</summary>
    [HttpPost]
    [Authorize(Policy = "sales.create")]
    [ProducesResponseType(typeof(ApiResponse<HeldSaleDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<HeldSaleDto>>> Hold([FromBody] HoldSaleDto dto)
    {
        await _featureEntitlement.EnsureFeatureEnabledAsync("sales.hold");
        var held = await _heldSaleService.HoldAsync(dto);
        return CreatedAtAction(
            nameof(GetById),
            new { id = held.Id },
            ApiResponse<HeldSaleDto>.SuccessResponse(held, "Sale held successfully"));
    }

    // ── GET /api/held-sales/{id} ──────────────────────────────────────────────

    /// <summary>Retrieve a single held sale (used to restore the cart on the POS screen).</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<HeldSaleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<HeldSaleDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<HeldSaleDto>>> GetById(long id)
    {
        var held = await _heldSaleService.GetByIdAsync(id);
        return Ok(ApiResponse<HeldSaleDto>.SuccessResponse(held));
    }

    // ── GET /api/held-sales/outlet/{outletId} ─────────────────────────────────

    /// <summary>List all parked sales for an outlet (shown in the "parked sales" panel).</summary>
    [HttpGet("outlet/{outletId}")]
    [ProducesResponseType(typeof(ApiResponse<HeldSaleListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<HeldSaleListDto>>> GetByOutlet(long outletId)
    {
        var list = await _heldSaleService.GetByOutletAsync(outletId);
        return Ok(ApiResponse<HeldSaleListDto>.SuccessResponse(list));
    }

    // ── GET /api/held-sales/cashier/{cashierId} ────────────────────────────────

    /// <summary>List all parked sales by a specific cashier.</summary>
    [HttpGet("cashier/{cashierId}")]
    [ProducesResponseType(typeof(ApiResponse<HeldSaleListDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<HeldSaleListDto>>> GetByCashier(long cashierId)
    {
        var list = await _heldSaleService.GetByCashierAsync(cashierId);
        return Ok(ApiResponse<HeldSaleListDto>.SuccessResponse(list));
    }

    // ── DELETE /api/held-sales/{id} ───────────────────────────────────────────

    /// <summary>
    /// Delete a held sale.
    /// Call this after the cart has been resumed and submitted, or when the cashier
    /// explicitly discards it.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "sales.create")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(long id)
    {
        await _heldSaleService.DeleteAsync(id);
        return Ok(ApiResponse.SuccessResponse("Held sale deleted"));
    }
}
