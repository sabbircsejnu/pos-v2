using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/purchase-orders")]
[Authorize]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderService _purchaseOrderService;

    public PurchaseOrdersController(IPurchaseOrderService purchaseOrderService)
    {
        _purchaseOrderService = purchaseOrderService;
    }

    /// <summary>
    /// Get all purchase orders with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<PurchaseOrderDto>>>> GetAll(
        [FromQuery] string? status = null,
        [FromQuery] long? supplierId = null,
        [FromQuery] long? warehouseId = null)
    {
        var pos = await _purchaseOrderService.GetAllAsync(status, supplierId, warehouseId);
        return Ok(ApiResponse<List<PurchaseOrderDto>>.SuccessResponse(pos));
    }

    /// <summary>
    /// Search purchase orders with pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderListDto>>> Search([FromBody] PurchaseOrderSearchDto searchDto)
    {
        var result = await _purchaseOrderService.SearchAsync(searchDto);
        return Ok(ApiResponse<PurchaseOrderListDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Get purchase order by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> GetById(long id)
    {
        var po = await _purchaseOrderService.GetByIdAsync(id);
        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po));
    }

    /// <summary>
    /// Get purchase orders awaiting approval
    /// </summary>
    [HttpGet("pending-approvals")]
    public async Task<ActionResult<ApiResponse<List<PurchaseOrderDto>>>> GetPendingApprovals()
    {
        var pos = await _purchaseOrderService.GetPendingApprovalsAsync();
        return Ok(ApiResponse<List<PurchaseOrderDto>>.SuccessResponse(pos));
    }

    /// <summary>
    /// Get total amount for purchase orders
    /// </summary>
    [HttpGet("total-amount")]
    public async Task<ActionResult<ApiResponse<decimal>>> GetTotalAmount(
        [FromQuery] string? status = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var total = await _purchaseOrderService.GetTotalAmountAsync(status, startDate, endDate);
        return Ok(ApiResponse<decimal>.SuccessResponse(total));
    }

    /// <summary>
    /// Create a new purchase order
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Create([FromBody] CreatePurchaseOrderDto dto)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedPoUserId) ? parsedPoUserId : null;

        var po = await _purchaseOrderService.CreateAsync(dto, userId);
        return CreatedAtAction(
            nameof(GetById),
            new { id = po.Id },
            ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order created successfully"));
    }

    /// <summary>
    /// Update an existing purchase order
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Update(long id, [FromBody] UpdatePurchaseOrderDto dto)
    {
        var po = await _purchaseOrderService.UpdateAsync(id, dto);
        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order updated successfully"));
    }

    /// <summary>
    /// Delete a purchase order (only if draft/pending with no GRNs)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(long id)
    {
        await _purchaseOrderService.DeleteAsync(id);
        return Ok(ApiResponse.SuccessResponse("Purchase order deleted successfully"));
    }

    /// <summary>
    /// Submit purchase order for approval (Draft → Pending)
    /// </summary>
    [HttpPost("{id}/submit")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Submit(long id)
    {
        var po = await _purchaseOrderService.SubmitForApprovalAsync(id);
        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order submitted for approval"));
    }

    /// <summary>
    /// Approve purchase order (Pending → Approved)
    /// </summary>
    [HttpPost("{id}/approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Approve(long id)
    {
        var po = await _purchaseOrderService.ApproveAsync(id);
        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order approved"));
    }

    /// <summary>
    /// Reject purchase order
    /// </summary>
    [HttpPost("{id}/reject")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Reject(long id, [FromBody] UpdatePurchaseOrderStatusDto dto)
    {
        var po = await _purchaseOrderService.RejectAsync(id, dto.Reason ?? "No reason provided");
        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order rejected"));
    }

    /// <summary>
    /// Cancel purchase order
    /// </summary>
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Cancel(long id, [FromBody] UpdatePurchaseOrderStatusDto dto)
    {
        var po = await _purchaseOrderService.CancelAsync(id, dto.Reason ?? "No reason provided");
        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order cancelled"));
    }
}
