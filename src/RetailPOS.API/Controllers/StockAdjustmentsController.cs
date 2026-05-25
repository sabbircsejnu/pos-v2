using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.StockAdjustment;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-adjustments")]
[Authorize]
public class StockAdjustmentsController : ControllerBase
{
    private readonly IStockAdjustmentService _adjustmentService;
    private readonly IUserOutletAccessService _outletAccess;

    public StockAdjustmentsController(
        IStockAdjustmentService adjustmentService,
        IUserOutletAccessService outletAccess)
    {
        _adjustmentService = adjustmentService;
        _outletAccess = outletAccess;
    }

    /// <summary>
    /// Get all stock adjustments with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<StockAdjustmentDto>>>> GetAll(
        [FromQuery] long? locationId = null,
        [FromQuery] string? locationType = null,
        [FromQuery] long? variantId = null)
    {
        var (resolvedId, resolvedType) =
            await _outletAccess.ResolveAndAuthorizeLocationAsync(locationId, locationType);

        var adjustments = await _adjustmentService.GetAllAsync(resolvedId, resolvedType, variantId);
        return Ok(ApiResponse<List<StockAdjustmentDto>>.SuccessResponse(adjustments));
    }

    /// <summary>
    /// Search stock adjustments with pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentListDto>>> Search([FromBody] StockAdjustmentSearchDto searchDto)
    {
        var (resolvedId, resolvedType) =
            await _outletAccess.ResolveAndAuthorizeLocationAsync(searchDto.LocationId, searchDto.LocationType);
        searchDto.LocationId = resolvedId;
        searchDto.LocationType = resolvedType;

        var result = await _adjustmentService.SearchAsync(searchDto);
        return Ok(ApiResponse<StockAdjustmentListDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Get stock adjustment history for a variant at a specific location
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<List<StockAdjustmentDto>>>> GetHistory(
        [FromQuery] long variantId,
        [FromQuery] long locationId)
    {
        // History is location-specific; non-BusinessOwner can only inspect their own outlet.
        await _outletAccess.ResolveAndAuthorizeLocationAsync(locationId, "outlet");
        var history = await _adjustmentService.GetHistoryAsync(variantId, locationId);
        return Ok(ApiResponse<List<StockAdjustmentDto>>.SuccessResponse(history));
    }

    /// <summary>
    /// Get stock adjustment by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> GetById(long id)
    {
        var adjustment = await _adjustmentService.GetByIdAsync(id);
        await _outletAccess.ResolveAndAuthorizeLocationAsync(adjustment.LocationId, adjustment.LocationType);
        return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(adjustment));
    }

    /// <summary>
    /// Create a stock adjustment (immediately updates inventory)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Create([FromBody] CreateStockAdjustmentDto dto)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null)
            throw new UnauthorizedAccessException("User identity not found");

        // Server-side enforcement: non-BusinessOwner is pinned to their default outlet
        // and cannot adjust warehouse stock. BusinessOwner is validated against their set.
        var (resolvedId, resolvedType) =
            await _outletAccess.EnforceWriteLocationAsync(dto.LocationId, dto.LocationType);
        dto.LocationId = resolvedId;
        dto.LocationType = resolvedType;

        var adjustedBy = long.Parse(userIdClaim);
        var adjustment = await _adjustmentService.CreateAsync(dto, adjustedBy);
        return CreatedAtAction(
            nameof(GetById),
            new { id = adjustment.Id },
            ApiResponse<StockAdjustmentDto>.SuccessResponse(adjustment, "Stock adjustment created and inventory updated"));
    }
}
