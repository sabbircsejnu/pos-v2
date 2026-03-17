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

    public StockAdjustmentsController(IStockAdjustmentService adjustmentService)
    {
        _adjustmentService = adjustmentService;
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
        var adjustments = await _adjustmentService.GetAllAsync(locationId, locationType, variantId);
        return Ok(ApiResponse<List<StockAdjustmentDto>>.SuccessResponse(adjustments));
    }

    /// <summary>
    /// Search stock adjustments with pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentListDto>>> Search([FromBody] StockAdjustmentSearchDto searchDto)
    {
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

        var adjustedBy = long.Parse(userIdClaim);
        var adjustment = await _adjustmentService.CreateAsync(dto, adjustedBy);
        return CreatedAtAction(
            nameof(GetById),
            new { id = adjustment.Id },
            ApiResponse<StockAdjustmentDto>.SuccessResponse(adjustment, "Stock adjustment created and inventory updated"));
    }
}
