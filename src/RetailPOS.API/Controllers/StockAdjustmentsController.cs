using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.StockAdjustment;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.Commands;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Application.Inventory.StockAdjustments;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-adjustments")]
[Authorize(Policy = "stock_adjustments.view")]
public class StockAdjustmentsController : ControllerBase
{
    private readonly IUserOutletAccessService _outletAccess;
    private readonly ISender _sender;

    public StockAdjustmentsController(
        IUserOutletAccessService outletAccess,
        ISender sender)
    {
        _outletAccess = outletAccess;
        _sender = sender;
    }

    /// <summary>
    /// Get all stock adjustments with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<StockAdjustmentDto>>>> GetAll(
        [FromQuery] long? locationId = null,
        [FromQuery] string? locationType = null,
        [FromQuery] long? variantId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (locationId.HasValue || !string.IsNullOrWhiteSpace(locationType))
        {
            (locationId, locationType) = await AuthorizeLocationFilterAsync(
                locationId,
                locationType,
                cancellationToken);
        }

        var adjustments = await _sender.Send(
            new GetStockAdjustmentsQuery(new StockAdjustmentListFilter(
                locationId,
                locationType,
                variantId,
                status)),
            cancellationToken);
        return Ok(ApiResponse<List<StockAdjustmentDto>>.SuccessResponse(
            adjustments.Select(StockAdjustmentDtoMapper.Map).ToList()));
    }

    /// <summary>
    /// Search stock adjustments with pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentListDto>>> Search(
        [FromBody] StockAdjustmentSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        if (searchDto.LocationId.HasValue || !string.IsNullOrWhiteSpace(searchDto.LocationType))
        {
            (searchDto.LocationId, searchDto.LocationType) = await AuthorizeLocationFilterAsync(
                searchDto.LocationId,
                searchDto.LocationType,
                cancellationToken);
        }

        var result = await _sender.Send(
            new SearchStockAdjustmentsQuery(new StockAdjustmentSearchFilter(
                searchDto.LocationId,
                searchDto.LocationType,
                searchDto.VariantId,
                searchDto.Status,
                searchDto.StartDate,
                searchDto.EndDate,
                searchDto.PageNumber,
                searchDto.PageSize)),
            cancellationToken);
        return Ok(ApiResponse<StockAdjustmentListDto>.SuccessResponse(new StockAdjustmentListDto
        {
            StockAdjustments = result.StockAdjustments.Select(StockAdjustmentDtoMapper.Map).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        }));
    }

    /// <summary>
    /// Get stock adjustment history for a variant at a specific location
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<List<StockAdjustmentDto>>>> GetHistory(
        [FromQuery] long variantId,
        [FromQuery] long locationId,
        CancellationToken cancellationToken)
    {
        var location = await AuthorizeLocationFilterAsync(
            locationId,
            null,
            cancellationToken);
        var history = await _sender.Send(
            new GetStockAdjustmentHistoryQuery(
                variantId,
                location.LocationId ?? locationId),
            cancellationToken);
        return Ok(ApiResponse<List<StockAdjustmentDto>>.SuccessResponse(
            history.Select(StockAdjustmentDtoMapper.Map).ToList()));
    }

    /// <summary>
    /// Get stock adjustment by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var adjustment = await _sender.Send(new GetStockAdjustmentQuery(id), cancellationToken);
        return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(StockAdjustmentDtoMapper.Map(adjustment)));
    }

    private async Task<(long? LocationId, string? LocationType)> AuthorizeLocationFilterAsync(
        long? locationId,
        string? locationType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (locationId.HasValue && string.IsNullOrWhiteSpace(locationType))
        {
            var authorized = await _outletAccess.GetAuthorizedOutletsAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var outletMatch = authorized.Outlets.Any(location => location.Id == locationId.Value);
            var warehouseMatch = authorized.Warehouses.Any(location => location.Id == locationId.Value);
            if (!outletMatch && !warehouseMatch)
            {
                throw new UnauthorizedAccessException(
                    $"You are not authorized to view data for location #{locationId.Value}.");
            }

            return (locationId, outletMatch ? "outlet" : "warehouse");
        }

        var resolved = await _outletAccess.ResolveAndAuthorizeLocationAsync(locationId, locationType);
        cancellationToken.ThrowIfCancellationRequested();
        return resolved;
    }

    private static string NormalizeCreationAction(string? action) =>
        string.IsNullOrWhiteSpace(action) ? StockAdjustmentCreateActions.Draft : action.Trim();

    /// <summary>
    /// Create a draft stock adjustment
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "stock_adjustments.create")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Create(
        [FromBody] CreateStockAdjustmentDto dto,
        CancellationToken cancellationToken)
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

            var adjustment = StockAdjustmentDtoMapper.Map(await _sender.Send(
                new CreateStockAdjustmentCommand(
                        string.Equals(
                            NormalizeCreationAction(dto.Action),
                            StockAdjustmentCreateActions.Submit,
                            StringComparison.OrdinalIgnoreCase),
                        locationId,
                        locationType,
                        adjustedBy,
                        null,
                        null,
                        [
                            new StockAdjustmentCreateLine(
                                dto.VariantId,
                                dto.QuantityChange,
                                dto.Reason,
                                dto.Notes)
                        ],
                        string.Equals(
                            NormalizeCreationAction(dto.Action),
                            StockAdjustmentCreateActions.SubmitAndApprove,
                            StringComparison.OrdinalIgnoreCase),
                        dto.Action),
                cancellationToken));
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
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> CreateBatch(
        [FromBody] CreateStockAdjustmentBatchDto dto,
        CancellationToken cancellationToken)
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

            var adjustment = StockAdjustmentDtoMapper.Map(await _sender.Send(
                new CreateStockAdjustmentCommand(
                        string.Equals(
                            NormalizeCreationAction(dto.Action),
                            StockAdjustmentCreateActions.Submit,
                            StringComparison.OrdinalIgnoreCase),
                        locationId,
                        locationType,
                        adjustedBy,
                        dto.Reason,
                        dto.Notes,
                        (dto.Items ?? [])
                            .Select(item => new StockAdjustmentCreateLine(
                                item.VariantId,
                                item.QuantityChange,
                                item.Reason,
                                item.Notes,
                                item.UnitOptionId,
                                item.EnteredQuantity,
                                item.ExpectedConfigVersionId,
                                item.PackState,
                                item.PackCode,
                                item.BaseQtyPerPack))
                            .ToArray(),
                        string.Equals(
                            NormalizeCreationAction(dto.Action),
                            StockAdjustmentCreateActions.SubmitAndApprove,
                            StringComparison.OrdinalIgnoreCase),
                        dto.Action),
                cancellationToken));
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
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Update(
        long id,
        [FromBody] UpdateStockAdjustmentDto dto,
        CancellationToken cancellationToken)
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

            var adjustment = StockAdjustmentDtoMapper.Map(await _sender.Send(
                new UpdateStockAdjustmentDraftCommand(
                    id,
                    updatedBy,
                    dto.LocationId,
                    dto.LocationType,
                    dto.Items.Select(item => new UpdateStockAdjustmentDraftLine(
                        item.VariantId,
                        item.QuantityChange,
                        item.Reason,
                        item.Notes,
                        item.UnitOptionId,
                        item.EnteredQuantity,
                        item.ExpectedConfigVersionId,
                        item.PackState,
                        item.PackCode,
                        item.BaseQtyPerPack)).ToArray()),
                cancellationToken));
            return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(
                adjustment,
                "Stock adjustment draft updated successfully"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse(ex.Message));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "stock_adjustments.delete")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var deletedBy))
        {
            return Unauthorized(ApiResponse<object>.ErrorResponse("User identity not found"));
        }

        try
        {
            await _sender.Send(
                new DeleteStockAdjustmentDraftCommand(id, deletedBy),
                cancellationToken);
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
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Submit(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var submittedBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        try
        {
            var adjustment = StockAdjustmentDtoMapper.Map(await _sender.Send(
                new SubmitStockAdjustmentCommand(id, submittedBy),
                cancellationToken));
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
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Approve(long id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var approvedBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        try
        {
            var adjustment = StockAdjustmentDtoMapper.Map(await _sender.Send(
                new ApproveStockAdjustmentCommand(id, approvedBy),
                cancellationToken));
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
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Reject(
        long id,
        [FromBody] RejectStockAdjustmentDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var rejectedBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        try
        {
            var adjustment = StockAdjustmentDtoMapper.Map(await _sender.Send(
                new RejectStockAdjustmentCommand(id, rejectedBy, dto.Reason),
                cancellationToken));
            return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(adjustment, "Stock adjustment rejected"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("{id}/reverse")]
    [Authorize(Policy = "stock_adjustments.reject")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Reverse(
        long id,
        [FromBody] RejectStockAdjustmentDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var reversedBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        try
        {
            var adjustment = StockAdjustmentDtoMapper.Map(await _sender.Send(
                new ReverseStockAdjustmentCommand(id, reversedBy, dto.Reason),
                cancellationToken));
            return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(
                adjustment,
                "Stock adjustment reversed"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockAdjustmentDto>.ErrorResponse(ex.Message));
        }
    }

    [HttpPost("{id}/cancel")]
    [Authorize(Policy = "stock_adjustments.edit")]
    public async Task<ActionResult<ApiResponse<StockAdjustmentDto>>> Cancel(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var cancelledBy))
        {
            return Unauthorized(ApiResponse<StockAdjustmentDto>.ErrorResponse("User identity not found"));
        }

        try
        {
            var adjustment = StockAdjustmentDtoMapper.Map(await _sender.Send(
                new CancelStockAdjustmentCommand(id, cancelledBy),
                cancellationToken));
            return Ok(ApiResponse<StockAdjustmentDto>.SuccessResponse(
                adjustment,
                "Stock adjustment cancelled"));
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
