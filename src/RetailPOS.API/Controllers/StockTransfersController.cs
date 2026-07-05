using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.StockTransfer;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-transfers")]
[Authorize(Policy = "stock_transfers.view")]
public class StockTransfersController : ControllerBase
{
    private readonly IStockTransferService _transferService;
    private readonly IUserOutletAccessService _outletAccess;

    public StockTransfersController(
        IStockTransferService transferService,
        IUserOutletAccessService outletAccess)
    {
        _transferService = transferService;
        _outletAccess = outletAccess;
    }

    /// <summary>
    /// Get all stock transfers with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<StockTransferDto>>>> GetAll(
        [FromQuery] string? status = null,
        [FromQuery] long? fromLocationId = null,
        [FromQuery] long? toLocationId = null)
    {
        // Non-BusinessOwner: scope to their outlet (transfers in OR out).
        // BusinessOwner: pass through, but validate any supplied filter.
        await EnforceListLocationFiltersAsync(fromLocationId, toLocationId);

        var transfers = await _transferService.GetAllAsync(status, fromLocationId, toLocationId);
        var scoped = await FilterByAuthorizedOutletsAsync(transfers);
        return Ok(ApiResponse<List<StockTransferDto>>.SuccessResponse(scoped));
    }

    /// <summary>
    /// Search stock transfers with pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockTransferListDto>>> Search([FromBody] StockTransferSearchDto searchDto)
    {
        await EnforceListLocationFiltersAsync(searchDto.FromLocationId, searchDto.ToLocationId);

        var result = await _transferService.SearchAsync(searchDto);
        result.StockTransfers = await FilterByAuthorizedOutletsAsync(result.StockTransfers);
        return Ok(ApiResponse<StockTransferListDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Get stock transfer by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> GetById(long id)
    {
        var transfer = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(transfer);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer));
    }

    /// <summary>
    /// Create a new stock transfer
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "stock_transfers.create")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Create([FromBody] CreateStockTransferDto dto)
    {
        // Users without the explicit permission are pinned to their default assigned source location.
        if (!User.HasPermission("stock_transfers.transfer_from_any_location"))
        {
            var auth = await _outletAccess.GetAuthorizedOutletsAsync();
            if (!auth.DefaultLocationId.HasValue || string.IsNullOrWhiteSpace(auth.DefaultLocationType))
                throw new UnauthorizedAccessException("Your account is not assigned to a default source location.");

            if (dto.FromLocationId != auth.DefaultLocationId.Value
                || !string.Equals(dto.FromLocationType, auth.DefaultLocationType, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("You are not allowed to create a transfer from the selected source location.");
            }
        }

        var (fromId, fromType) = await _outletAccess.EnforceWriteLocationAsync(dto.FromLocationId, dto.FromLocationType);
        dto.FromLocationId = fromId;
        dto.FromLocationType = fromType;

        ValidateLocationType(dto.ToLocationType);
        await ValidateDestinationLocationAsync(dto.ToLocationId, dto.ToLocationType);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var transfer = await _transferService.CreateAsync(dto, userId);
        return CreatedAtAction(
            nameof(GetById),
            new { id = transfer.Id },
            ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer created successfully"));
    }

    /// <summary>
    /// Update a stock transfer (only if pending)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "stock_transfers.edit")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Update(long id, [FromBody] CreateStockTransferDto dto)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(existing);

        if (!User.HasPermission("stock_transfers.transfer_from_any_location"))
        {
            var auth = await _outletAccess.GetAuthorizedOutletsAsync();
            if (!auth.DefaultLocationId.HasValue || string.IsNullOrWhiteSpace(auth.DefaultLocationType))
                throw new UnauthorizedAccessException("Your account is not assigned to a default source location.");

            if (dto.FromLocationId != auth.DefaultLocationId.Value
                || !string.Equals(dto.FromLocationType, auth.DefaultLocationType, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("You are not allowed to update a transfer with the selected source location.");
            }
        }

        var (fromId, fromType) = await _outletAccess.EnforceWriteLocationAsync(dto.FromLocationId, dto.FromLocationType);
        dto.FromLocationId = fromId;
        dto.FromLocationType = fromType;

        ValidateLocationType(dto.ToLocationType);
        await ValidateDestinationLocationAsync(dto.ToLocationId, dto.ToLocationType);

        var transfer = await _transferService.UpdateAsync(id, dto);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer updated successfully"));
    }

    /// <summary>
    /// Submit stock transfer (already pending on creation - provided for workflow completeness)
    /// </summary>
    [HttpPost("{id}/submit")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Submit(long id)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(existing);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var transfer = await _transferService.SubmitAsync(id, userId);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer submitted"));
    }

    /// <summary>
    /// Approve a stock transfer (pending → approved)
    /// </summary>
    [HttpPost("{id}/approve")]
    [Authorize(Policy = "stock_transfers.approve")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Approve(long id)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(existing);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedApproverId) ? parsedApproverId : null;

        var transfer = await _transferService.ApproveAsync(id, userId);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer approved"));
    }

    /// <summary>
    /// Reject a stock transfer
    /// </summary>
    [HttpPost("{id}/reject")]
    [Authorize(Policy = "stock_transfers.reject_receive")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Reject(long id, [FromBody] TransferStatusUpdateDto dto)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureCanReceiveTransferAsync(existing);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? rejectedBy = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var transfer = await _transferService.RejectAsync(id, dto.Reason, rejectedBy);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer rejected"));
    }

    /// <summary>
    /// Mark a stock transfer as in-transit (approved → in_transit)
    /// </summary>
    [HttpPost("{id}/send")]
    [Authorize(Policy = "stock_transfers.dispatch")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Send(long id)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureCanSendTransferAsync(existing);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var transfer = await _transferService.SendAsync(id, userId);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer marked as in-transit"));
    }

    /// <summary>
    /// Mark a stock transfer as in-transit (approved → in_transit)
    /// </summary>
    [HttpPost("{id}/dispatch")]
    [Authorize(Policy = "stock_transfers.dispatch")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Dispatch(long id)
    {
        return await Send(id);
    }

    /// <summary>
    /// Receive a stock transfer and update inventory (in_transit → received)
    /// </summary>
    [HttpPost("{id}/receive")]
    [Authorize(Policy = "stock_transfers.receive")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Receive(long id, [FromBody] ReceiveStockTransferDto? dto)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureCanReceiveTransferAsync(existing);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var transfer = await _transferService.ReceiveAsync(id, dto, userId);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer received and inventory updated"));
    }

    /// <summary>
    /// Cancel a stock transfer
    /// </summary>
    [HttpPost("{id}/cancel")]
    [Authorize(Policy = "stock_transfers.cancel")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Cancel(long id, [FromBody] TransferStatusUpdateDto dto)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(existing);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

        var transfer = await _transferService.CancelAsync(id, dto.Reason, userId);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer cancelled"));
    }

    private async Task EnforceListLocationFiltersAsync(long? fromLocationId, long? toLocationId)
    {
        if (fromLocationId.HasValue)
            await _outletAccess.ResolveAndAuthorizeLocationAsync(fromLocationId, null);
        if (toLocationId.HasValue)
            await _outletAccess.ResolveAndAuthorizeLocationAsync(toLocationId, null);
    }

    private async Task EnsureTransferAuthorizedAsync(StockTransferDto transfer)
    {
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        if (auth.IsGlobalAccess) return;

        var allowedOutletIds = auth.Outlets.Select(o => o.Id).ToHashSet();
        var allowedWarehouseIds = auth.Warehouses.Select(w => w.Id).ToHashSet();

        var fromMatches = transfer.FromLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
            ? allowedOutletIds.Contains(transfer.FromLocationId)
            : transfer.FromLocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase)
                && allowedWarehouseIds.Contains(transfer.FromLocationId);

        var toMatches = transfer.ToLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
            ? allowedOutletIds.Contains(transfer.ToLocationId)
            : transfer.ToLocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase)
                && allowedWarehouseIds.Contains(transfer.ToLocationId);

        if (!fromMatches && !toMatches)
            throw new UnauthorizedAccessException(
                "You are not authorized to view or modify this stock transfer.");
    }

    private async Task<List<StockTransferDto>> FilterByAuthorizedOutletsAsync(List<StockTransferDto> transfers)
    {
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        if (auth.IsGlobalAccess) return transfers;

        var allowedOutletIds = auth.Outlets.Select(o => o.Id).ToHashSet();
        var allowedWarehouseIds = auth.Warehouses.Select(w => w.Id).ToHashSet();

        if (allowedOutletIds.Count == 0 && allowedWarehouseIds.Count == 0)
            return new List<StockTransferDto>();

        return transfers
            .Where(t =>
                (t.FromLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase) && allowedOutletIds.Contains(t.FromLocationId))
                || (t.ToLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase) && allowedOutletIds.Contains(t.ToLocationId))
                || (t.FromLocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase) && allowedWarehouseIds.Contains(t.FromLocationId))
                || (t.ToLocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase) && allowedWarehouseIds.Contains(t.ToLocationId)))
            .ToList();
    }

    private async Task EnsureCanSendTransferAsync(StockTransferDto transfer)
    {
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        if (auth.IsGlobalAccess) return;

        var allowedOutletIds = auth.Outlets.Select(o => o.Id).ToHashSet();
        var allowedWarehouseIds = auth.Warehouses.Select(w => w.Id).ToHashSet();

        var canSend = transfer.FromLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
            ? allowedOutletIds.Contains(transfer.FromLocationId)
            : transfer.FromLocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase)
                && allowedWarehouseIds.Contains(transfer.FromLocationId);

        if (!canSend)
            throw new UnauthorizedAccessException(
                "You are not authorized to send this transfer from the source location.");
    }

    private async Task EnsureCanReceiveTransferAsync(StockTransferDto transfer)
    {
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        if (auth.IsGlobalAccess)
            return;

        var normalizedType = transfer.ToLocationType.Trim().ToLowerInvariant();
        var isAuthorizedDestination = normalizedType == "outlet"
            ? auth.Outlets.Any(o => o.Id == transfer.ToLocationId)
            : normalizedType == "warehouse"
                ? auth.Warehouses.Any(w => w.Id == transfer.ToLocationId)
                : false;

        if (!isAuthorizedDestination)
            throw new UnauthorizedAccessException("You are not authorized to receive or reject transfers for this destination location.");
    }

    private static void ValidateLocationType(string? locationType)
    {
        var normalizedType = (locationType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedType != "outlet" && normalizedType != "warehouse")
            throw new InvalidOperationException("Location type must be either outlet or warehouse.");
    }

    private async Task ValidateDestinationLocationAsync(long locationId, string locationType)
    {
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        var normalizedType = locationType.Trim().ToLowerInvariant();

        var destinationOutlets = auth.DestinationOutlets ?? auth.Outlets;
        var destinationWarehouses = auth.DestinationWarehouses ?? auth.Warehouses;

        var isAllowed = normalizedType == "outlet"
            ? destinationOutlets.Any(o => o.Id == locationId)
            : destinationWarehouses.Any(w => w.Id == locationId);

        if (!isAllowed)
            throw new UnauthorizedAccessException($"Destination {normalizedType} #{locationId} is not available in your business scope.");
    }
}
