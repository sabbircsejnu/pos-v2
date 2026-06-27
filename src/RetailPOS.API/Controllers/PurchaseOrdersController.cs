using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.Documents.Pdf;
using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/purchase-orders")]
[Authorize(Policy = "purchases.view")]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderService _purchaseOrderService;
    private readonly IPurchaseOrderPdfService _purchaseOrderPdfService;
    private readonly IAuditContext _auditCtx;
    private readonly IAuditService _auditSvc;

    public PurchaseOrdersController(IPurchaseOrderService purchaseOrderService,
        IPurchaseOrderPdfService purchaseOrderPdfService,
        IAuditContext auditCtx, IAuditService auditSvc)
    {
        _purchaseOrderService = purchaseOrderService;
        _purchaseOrderPdfService = purchaseOrderPdfService;
        _auditCtx = auditCtx;
        _auditSvc = auditSvc;
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
    /// Download purchase order as PDF
    /// </summary>
    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> DownloadPdf(long id, CancellationToken cancellationToken)
    {
        var pdf = await _purchaseOrderPdfService.GenerateAsync(id, User.CanViewCost(), cancellationToken);
        return File(pdf.Content, "application/pdf", pdf.FileName);
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
    [Authorize(Policy = "purchases.create")]
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
    /// Create purchase order and immediately receive stock (auto-GRN).
    /// </summary>
    [HttpPost("purchase-receive")]
    [Authorize(Policy = "purchases.receive")]
    public async Task<ActionResult<ApiResponse<PurchaseAndReceiveResultDto>>> PurchaseAndReceive([FromBody] CreatePurchaseAndReceiveDto dto)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedPoUserId) ? parsedPoUserId : null;

        if (!userId.HasValue)
        {
            return Unauthorized(ApiResponse<PurchaseAndReceiveResultDto>.ErrorResponse("Authenticated user context is required for Purchase & Receive."));
        }

        _auditCtx.BeginScope(
            "Purchase & Receive Immediate", AuditActionType.Create,
            AuditModule.Purchase, "PurchaseOrder", null);

        try
        {
            var result = await _purchaseOrderService.PurchaseAndReceiveAsync(dto, userId);
            await _auditSvc.FlushScopeAsync();

            var message = result.IsDuplicateRequest
                ? "Duplicate request detected; existing purchase receive result returned"
                : "Purchase order created, auto-GRN generated, and stock updated successfully";

            return Ok(ApiResponse<PurchaseAndReceiveResultDto>.SuccessResponse(result, message));
        }
        catch (Exception ex)
        {
            _auditCtx.FailScope(ex.Message);
            await _auditSvc.FlushScopeAsync();
            throw;
        }
    }

    /// <summary>
    /// Update an existing purchase order
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "purchases.edit")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Update(long id, [FromBody] UpdatePurchaseOrderDto dto)
    {
        var po = await _purchaseOrderService.UpdateAsync(id, dto);
        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order updated successfully"));
    }

    /// <summary>
    /// Delete a purchase order (only if draft/pending with no GRNs)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "purchases.edit")]
    public async Task<ActionResult<ApiResponse>> Delete(long id)
    {
        await _purchaseOrderService.DeleteAsync(id);
        return Ok(ApiResponse.SuccessResponse("Purchase order deleted successfully"));
    }

    /// <summary>
    /// Submit purchase order for approval (Draft → Pending)
    /// </summary>
    [HttpPost("{id}/submit")]
    [Authorize(Policy = "purchases.approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Submit(long id)
    {
        _auditCtx.BeginScope(
            "Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", id.ToString());

        var po = await _purchaseOrderService.SubmitForApprovalAsync(id);
        await _auditSvc.FlushScopeAsync();

        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order submitted for approval"));
    }

    /// <summary>
    /// Approve purchase order (Pending → Approved)
    /// </summary>
    [HttpPost("{id}/approve")]
    [Authorize(Policy = "purchases.approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Approve(long id)
    {
        _auditCtx.BeginScope(
            "Approve Purchase Order", AuditActionType.Approve,
            AuditModule.Purchase, "PurchaseOrder", id.ToString());

        var po = await _purchaseOrderService.ApproveAsync(id);
        await _auditSvc.FlushScopeAsync();

        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order approved"));
    }

    /// <summary>
    /// Reject purchase order
    /// </summary>
    [HttpPost("{id}/reject")]
    [Authorize(Policy = "purchases.approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Reject(long id, [FromBody] UpdatePurchaseOrderStatusDto dto)
    {
        _auditCtx.BeginScope(
            "Reject Purchase Order", AuditActionType.Reject,
            AuditModule.Purchase, "PurchaseOrder", id.ToString());

        var po = await _purchaseOrderService.RejectAsync(id, dto.Reason ?? "No reason provided");
        await _auditSvc.FlushScopeAsync();

        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order rejected"));
    }

    /// <summary>
    /// Cancel purchase order
    /// </summary>
    [HttpPost("{id}/cancel")]
    [Authorize(Policy = "purchases.approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Cancel(long id, [FromBody] UpdatePurchaseOrderStatusDto dto)
    {
        _auditCtx.BeginScope(
            "Cancel Purchase Order", AuditActionType.Cancel,
            AuditModule.Purchase, "PurchaseOrder", id.ToString());

        var po = await _purchaseOrderService.CancelAsync(id, dto.Reason ?? "No reason provided");
        await _auditSvc.FlushScopeAsync();

        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order cancelled"));
    }

    /// <summary>
    /// Send purchase order back for correction (Pending → Sent Back)
    /// </summary>
    [HttpPost("{id}/send-back")]
    [Authorize(Policy = "purchases.approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> SendBack(long id, [FromBody] UpdatePurchaseOrderStatusDto dto)
    {
        _auditCtx.BeginScope(
            "Send Back Purchase Order", AuditActionType.Update,
            AuditModule.Purchase, "PurchaseOrder", id.ToString());

        var po = await _purchaseOrderService.SendBackAsync(id, dto.Reason ?? "No reason provided");
        await _auditSvc.FlushScopeAsync();

        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order sent back for correction"));
    }
}
