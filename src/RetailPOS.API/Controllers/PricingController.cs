using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Pricing;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/pricing")]
[Authorize(Policy = "products.view")]
public class PricingController : ControllerBase
{
    private readonly IPricingService _pricingService;

    public PricingController(IPricingService pricingService)
    {
        _pricingService = pricingService;
    }

    // ── Core price calculation ────────────────────────────────────────────

    /// <summary>
    /// Calculate the effective price for a single cart line.
    /// Called by POS when a product is added, and by reports for margin calculation.
    /// </summary>
    [HttpPost("calculate")]
    public async Task<ActionResult<ApiResponse<PriceCalculationResultDto>>> Calculate(
        [FromBody] PriceCalculationRequestDto dto)
    {
        var result = await _pricingService.CalculateAsync(dto);
        return Ok(ApiResponse<PriceCalculationResultDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Calculate prices for multiple cart lines in one call.
    /// Use this from the POS screen to avoid N+1 HTTP round trips when building a cart.
    /// </summary>
    [HttpPost("calculate-batch")]
    public async Task<ActionResult<ApiResponse<List<PriceCalculationResultDto>>>> CalculateBatch(
        [FromBody] PriceCalculationBatchRequestDto dto)
    {
        var results = await _pricingService.CalculateBatchAsync(dto);
        return Ok(ApiResponse<List<PriceCalculationResultDto>>.SuccessResponse(results));
    }

    /// <summary>
    /// GET variant for fast POS barcode-scan lookup.
    /// GET /api/pricing/product/{variantId}?outletId=1&amp;quantity=2
    /// </summary>
    [HttpGet("product/{variantId:long}")]
    public async Task<ActionResult<ApiResponse<PriceCalculationResultDto>>> GetForVariant(
        long variantId,
        [FromQuery] long? outletId = null,
        [FromQuery] int quantity   = 1)
    {
        var result = await _pricingService.CalculateAsync(new PriceCalculationRequestDto
        {
            ProductVariantId = variantId,
            OutletId         = outletId,
            Quantity         = Math.Max(1, quantity)
        });
        return Ok(ApiResponse<PriceCalculationResultDto>.SuccessResponse(result));
    }

    // ── Price Rules ──────────────────────────────────────────────────────

    /// <summary>Search and list price/campaign rules with optional filters.</summary>
    [HttpPost("rules/search")]
    public async Task<ActionResult<ApiResponse<PriceRuleListDto>>> SearchRules(
        [FromBody] PriceRuleSearchDto searchDto)
    {
        var result = await _pricingService.SearchRulesAsync(searchDto);
        return Ok(ApiResponse<PriceRuleListDto>.SuccessResponse(result));
    }

    /// <summary>Get a price rule by ID.</summary>
    [HttpGet("rules/{id:long}")]
    public async Task<ActionResult<ApiResponse<PriceRuleDto>>> GetRule(long id)
    {
        var rule = await _pricingService.GetRuleByIdAsync(id);
        return Ok(ApiResponse<PriceRuleDto>.SuccessResponse(rule));
    }

    /// <summary>Create a new price/campaign rule.</summary>
    [HttpPost("rules")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<PriceRuleDto>>> CreateRule(
        [FromBody] CreatePriceRuleDto dto)
    {
        var rule = await _pricingService.CreateRuleAsync(dto);
        return CreatedAtAction(
            nameof(GetRule),
            new { id = rule.Id },
            ApiResponse<PriceRuleDto>.SuccessResponse(rule, "Price rule created successfully"));
    }

    /// <summary>Update a price rule.</summary>
    [HttpPut("rules/{id:long}")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<PriceRuleDto>>> UpdateRule(
        long id, [FromBody] UpdatePriceRuleDto dto)
    {
        var rule = await _pricingService.UpdateRuleAsync(id, dto);
        return Ok(ApiResponse<PriceRuleDto>.SuccessResponse(rule, "Price rule updated successfully"));
    }

    /// <summary>Delete a price rule.</summary>
    [HttpDelete("rules/{id:long}")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse>> DeleteRule(long id)
    {
        await _pricingService.DeleteRuleAsync(id);
        return Ok(ApiResponse.SuccessResponse("Price rule deleted successfully"));
    }

    // ── Outlet Price Overrides ────────────────────────────────────────────

    /// <summary>Get all price overrides configured for a specific outlet.</summary>
    [HttpGet("outlet-overrides/outlet/{outletId:long}")]
    public async Task<ActionResult<ApiResponse<OutletPriceOverrideListDto>>> GetByOutlet(long outletId)
    {
        var result = await _pricingService.GetOverridesByOutletAsync(outletId);
        return Ok(ApiResponse<OutletPriceOverrideListDto>.SuccessResponse(result));
    }

    /// <summary>Get an outlet price override by ID.</summary>
    [HttpGet("outlet-overrides/{id:long}")]
    public async Task<ActionResult<ApiResponse<OutletPriceOverrideDto>>> GetOverride(long id)
    {
        var result = await _pricingService.GetOverrideByIdAsync(id);
        return Ok(ApiResponse<OutletPriceOverrideDto>.SuccessResponse(result));
    }

    /// <summary>Create an outlet price override.</summary>
    [HttpPost("outlet-overrides")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<OutletPriceOverrideDto>>> CreateOverride(
        [FromBody] CreateOutletPriceOverrideDto dto)
    {
        var result = await _pricingService.CreateOverrideAsync(dto);
        return CreatedAtAction(
            nameof(GetOverride),
            new { id = result.Id },
            ApiResponse<OutletPriceOverrideDto>.SuccessResponse(result, "Outlet price override created successfully"));
    }

    /// <summary>Update an outlet price override.</summary>
    [HttpPut("outlet-overrides/{id:long}")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse<OutletPriceOverrideDto>>> UpdateOverride(
        long id, [FromBody] UpdateOutletPriceOverrideDto dto)
    {
        var result = await _pricingService.UpdateOverrideAsync(id, dto);
        return Ok(ApiResponse<OutletPriceOverrideDto>.SuccessResponse(result, "Outlet price override updated successfully"));
    }

    /// <summary>Delete an outlet price override.</summary>
    [HttpDelete("outlet-overrides/{id:long}")]
    [Authorize(Policy = "products.edit")]
    public async Task<ActionResult<ApiResponse>> DeleteOverride(long id)
    {
        await _pricingService.DeleteOverrideAsync(id);
        return Ok(ApiResponse.SuccessResponse("Outlet price override deleted successfully"));
    }
}
