using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.StockTransfer;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-transfers")]
[Authorize]
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
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Create([FromBody] CreateStockTransferDto dto)
    {
        // Non-BusinessOwner: only transfers OUT of their default outlet are permitted;
        // destination must still be a location they could read (validate via authorize).
        var (fromId, fromType) = await _outletAccess.EnforceWriteLocationAsync(dto.FromLocationId, dto.FromLocationType);
        dto.FromLocationId = fromId;
        dto.FromLocationType = fromType;

        // Destination doesn't have to be the user's outlet, but must still be in their
        // authorized set (BusinessOwner: any; others: only their own outlet).
        var (toId, toType) = await _outletAccess.ResolveAndAuthorizeLocationAsync(dto.ToLocationId, dto.ToLocationType);
        if (!toId.HasValue || string.IsNullOrEmpty(toType))
            throw new InvalidOperationException("Destination location is required.");
        dto.ToLocationId = toId.Value;
        dto.ToLocationType = toType;

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
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Update(long id, [FromBody] CreateStockTransferDto dto)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(existing);

        var (fromId, fromType) = await _outletAccess.EnforceWriteLocationAsync(dto.FromLocationId, dto.FromLocationType);
        dto.FromLocationId = fromId;
        dto.FromLocationType = fromType;

        var (toId, toType) = await _outletAccess.ResolveAndAuthorizeLocationAsync(dto.ToLocationId, dto.ToLocationType);
        if (!toId.HasValue || string.IsNullOrEmpty(toType))
            throw new InvalidOperationException("Destination location is required.");
        dto.ToLocationId = toId.Value;
        dto.ToLocationType = toType;

        var transfer = await _transferService.UpdateAsync(id, dto);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer updated successfully"));
    }

    /// <summary>
    /// Submit stock transfer (already pending on creation - provided for workflow completeness)
    /// </summary>
    [HttpPost("{id}/submit")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Submit(long id)
    {
        var transfer = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(transfer);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer is already submitted (pending)"));
    }

    /// <summary>
    /// Approve a stock transfer (pending → approved)
    /// </summary>
    [HttpPost("{id}/approve")]
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
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Reject(long id, [FromBody] TransferStatusUpdateDto dto)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(existing);

        var transfer = await _transferService.RejectAsync(id, dto.Reason);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer rejected"));
    }

    /// <summary>
    /// Mark a stock transfer as in-transit (approved → in_transit)
    /// </summary>
    [HttpPost("{id}/send")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Send(long id)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(existing);

        var transfer = await _transferService.SendAsync(id);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer marked as in-transit"));
    }

    /// <summary>
    /// Receive a stock transfer and update inventory (in_transit → received)
    /// </summary>
    [HttpPost("{id}/receive")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Receive(long id)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(existing);

        var transfer = await _transferService.ReceiveAsync(id);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer received and inventory updated"));
    }

    /// <summary>
    /// Cancel a stock transfer
    /// </summary>
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Cancel(long id, [FromBody] TransferStatusUpdateDto dto)
    {
        var existing = await _transferService.GetByIdAsync(id);
        await EnsureTransferAuthorizedAsync(existing);

        var transfer = await _transferService.CancelAsync(id, dto.Reason);
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
        if (auth.IsBusinessOwner) return;

        var allowedOutlet = auth.DefaultOutletId
            ?? throw new UnauthorizedAccessException(
                "Your account is not assigned to a default outlet.");

        var fromMatches = transfer.FromLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
                          && transfer.FromLocationId == allowedOutlet;
        var toMatches = transfer.ToLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
                        && transfer.ToLocationId == allowedOutlet;

        if (!fromMatches && !toMatches)
            throw new UnauthorizedAccessException(
                "You are not authorized to view or modify this stock transfer.");
    }

    private async Task<List<StockTransferDto>> FilterByAuthorizedOutletsAsync(List<StockTransferDto> transfers)
    {
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        if (auth.IsBusinessOwner) return transfers;

        var allowedOutlet = auth.DefaultOutletId;
        if (!allowedOutlet.HasValue) return new List<StockTransferDto>();

        return transfers
            .Where(t =>
                (t.FromLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase) && t.FromLocationId == allowedOutlet.Value)
                || (t.ToLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase) && t.ToLocationId == allowedOutlet.Value))
            .ToList();
    }
}
