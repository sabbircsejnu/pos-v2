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

    public StockTransfersController(IStockTransferService transferService)
    {
        _transferService = transferService;
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
        var transfers = await _transferService.GetAllAsync(status, fromLocationId, toLocationId);
        return Ok(ApiResponse<List<StockTransferDto>>.SuccessResponse(transfers));
    }

    /// <summary>
    /// Search stock transfers with pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockTransferListDto>>> Search([FromBody] StockTransferSearchDto searchDto)
    {
        var result = await _transferService.SearchAsync(searchDto);
        return Ok(ApiResponse<StockTransferListDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Get stock transfer by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> GetById(long id)
    {
        var transfer = await _transferService.GetByIdAsync(id);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer));
    }

    /// <summary>
    /// Create a new stock transfer
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Create([FromBody] CreateStockTransferDto dto)
    {
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
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer is already submitted (pending)"));
    }

    /// <summary>
    /// Approve a stock transfer (pending → approved)
    /// </summary>
    [HttpPost("{id}/approve")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Approve(long id)
    {
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
        var transfer = await _transferService.RejectAsync(id, dto.Reason);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer rejected"));
    }

    /// <summary>
    /// Mark a stock transfer as in-transit (approved → in_transit)
    /// </summary>
    [HttpPost("{id}/send")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Send(long id)
    {
        var transfer = await _transferService.SendAsync(id);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer marked as in-transit"));
    }

    /// <summary>
    /// Receive a stock transfer and update inventory (in_transit → received)
    /// </summary>
    [HttpPost("{id}/receive")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Receive(long id)
    {
        var transfer = await _transferService.ReceiveAsync(id);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer received and inventory updated"));
    }

    /// <summary>
    /// Cancel a stock transfer
    /// </summary>
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> Cancel(long id, [FromBody] TransferStatusUpdateDto dto)
    {
        var transfer = await _transferService.CancelAsync(id, dto.Reason);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Stock transfer cancelled"));
    }
}
