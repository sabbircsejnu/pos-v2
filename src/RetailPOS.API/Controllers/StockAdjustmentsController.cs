using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.StockAdjustment;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-adjustments")]
[Authorize(Policy = "stock_adjustments.view")]
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
        [FromQuery] long? variantId = null,
        [FromQuery] string? status = null)
    {
        var adjustments = await _adjustmentService.GetAllAsync(locationId, locationType, variantId, status);
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
    /// Create a draft stock adjustment
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "stock_adjustments.create")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Create([FromBody] CreateStockAdjustmentDto dto)
    {
        if (!TryGetCurrentUserId(out var adjustedBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        if (string.Equals(dto.Action, StockAdjustmentCreateActions.SubmitAndApprove, StringComparison.OrdinalIgnoreCase)
            && !User.HasPermission("stock_adjustments.approve"))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse("You do not have permission to approve stock adjustments"));
        }

        try
        {
            var (locationId, locationType) = await _outletAccess.EnforceWriteLocationAsync(dto.LocationId, dto.LocationType);
            dto.LocationId = locationId;
            dto.LocationType = locationType;

            var adjustment = await _adjustmentService.CreateAsync(dto, adjustedBy);
            return CreatedAtAction(
                nameof(GetById),
                new { id = adjustment.Id },
                ApiResponse<StockAdjustmentDto>.SuccessResponse(
                    adjustment,
                    $"Stock adjustment {adjustment.AdjustmentNumber} created with status {adjustment.Status}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse(ex.Message));
        }
    }

    /// <summary>
    /// Create multiple stock adjustments in a single submission
    /// </summary>
    [HttpPost("batch")]
    [Authorize(Policy = "stock_adjustments.create")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> CreateBatch([FromBody] CreateStockAdjustmentBatchDto dto)
    {
        if (!TryGetCurrentUserId(out var adjustedBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        if (string.Equals(dto.Action, StockAdjustmentCreateActions.SubmitAndApprove, StringComparison.OrdinalIgnoreCase)
            && !User.HasPermission("stock_adjustments.approve"))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse("You do not have permission to approve stock adjustments"));
        }

        try
        {
            var (locationId, locationType) = await _outletAccess.EnforceWriteLocationAsync(dto.LocationId, dto.LocationType);
            dto.LocationId = locationId;
            dto.LocationType = locationType;

            var adjustment = await _adjustmentService.CreateBatchAsync(dto, adjustedBy);
            return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(
                adjustment,
                $"Stock adjustment {adjustment.AdjustmentNumber} created with status {adjustment.Status}"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse(ex.Message));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "stock_adjustments.edit")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Update(long id, [FromBody] UpdateStockAdjustmentDto dto)
    {
        if (!TryGetCurrentUserId(out var updatedBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        try
        {
            var (locationId, locationType) = await _outletAccess.EnforceWriteLocationAsync(dto.LocationId, dto.LocationType);
            dto.LocationId = locationId;
            dto.LocationType = locationType;

            var existing = await _adjustmentService.GetByIdAsync(id);

            var adjustment = await _adjustmentService.UpdateAsync(id, dto, updatedBy);
            return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(adjustment, "Stock adjustment draft updated successfully"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse(ex.Message));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "stock_adjustments.delete")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(long id)
    {
        if (!TryGetCurrentUserId(out var deletedBy))
        {
            return Unauthorized(ApiResponse<object>.ErrorResponse("User identity not found"));
        }

        try
        {
            var existing = await _adjustmentService.GetByIdAsync(id);
            await _adjustmentService.DeleteAsync(id, deletedBy);
            return Ok(ApiResponse<object>.SuccessResponse(new { id }, "Stock adjustment draft deleted successfully"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<object>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("{id}/submit")]
    [Authorize(Policy = "stock_adjustments.create")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Submit(long id)
    {
        if (!TryGetCurrentUserId(out var submittedBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        try
        {
            var existing = await _adjustmentService.GetByIdAsync(id);
            var adjustment = await _adjustmentService.SubmitAsync(id, submittedBy);
            return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(adjustment, "Stock adjustment submitted for approval"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("{id}/approve")]
    [Authorize(Policy = "stock_adjustments.approve")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Approve(long id)
    {
        if (!TryGetCurrentUserId(out var approvedBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        try
        {
            var existing = await _adjustmentService.GetByIdAsync(id);
            var adjustment = await _adjustmentService.ApproveAsync(id, approvedBy);
            return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(adjustment, "Stock adjustment approved and inventory updated"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("{id}/reject")]
    [Authorize(Policy = "stock_adjustments.reject")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Reject(long id, [FromBody] RejectStockAdjustmentDto dto)
    {
        if (!TryGetCurrentUserId(out var rejectedBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        try
        {
            var existing = await _adjustmentService.GetByIdAsync(id);
            var adjustment = await _adjustmentService.RejectAsync(id, rejectedBy, dto.Reason);
            return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(adjustment, "Stock adjustment rejected"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("{id}/cancel")]
    [Authorize(Policy = "stock_adjustments.edit")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Cancel(long id)
    {
        if (!TryGetCurrentUserId(out var cancelledBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        try
        {
            var existing = await _adjustmentService.GetByIdAsync(id);
            var adjustment = await _adjustmentService.CancelAsync(id, cancelledBy);
            return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(adjustment, "Stock adjustment cancelled"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse(ex.Message));
        }
    }

    private bool TryGetCurrentUserId(out long userId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return long.TryParse(userIdClaim, out userId);
    }
}
