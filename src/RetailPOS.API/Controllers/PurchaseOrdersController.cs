using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.Documents.Pdf;
using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.PurchaseOrders.Queries;
using RetailPOS.Application.PurchaseOrders.ReadModels;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Application.AuditEvents.Commands;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using MediatR;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/purchase-orders")]
[Authorize(Policy = "purchases.view")]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderPdfService _purchaseOrderPdfService;
    private readonly ISender _sender;
    private readonly IAuditContext _auditCtx;

    public PurchaseOrdersController(IPurchaseOrderPdfService purchaseOrderPdfService,
        IAuditContext auditCtx,
        ISender sender)
    {
        _purchaseOrderPdfService = purchaseOrderPdfService;
        _auditCtx = auditCtx;
        _sender = sender;
    }

    /// <summary>
    /// Get all purchase orders with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<PurchaseOrderDto>>>> GetAll(
        CancellationToken cancellationToken,
        [FromQuery] string? status = null,
        [FromQuery] long? supplierId = null,
        [FromQuery] long? warehouseId = null,
        [FromQuery] string? locationType = null,
        [FromQuery] long? locationId = null)
    {
        var purchaseOrders = await _sender.Send(
            new GetPurchaseOrdersQuery(status, supplierId, warehouseId, locationType, locationId),
            cancellationToken);
        var pos = purchaseOrders.Select(MapPurchaseOrder).ToList();
        return Ok(ApiResponse<List<PurchaseOrderDto>>.SuccessResponse(pos));
    }

    /// <summary>
    /// Search purchase orders with pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderListDto>>> Search(
        [FromBody] PurchaseOrderSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var searchResult = await _sender.Send(
            new SearchPurchaseOrdersQuery(
                searchDto.Status,
                searchDto.SupplierId,
                searchDto.WarehouseId,
                searchDto.StartDate,
                searchDto.EndDate,
                searchDto.PageNumber,
                searchDto.PageSize,
                searchDto.SortBy,
                searchDto.SortOrder,
                searchDto.LocationType,
                searchDto.LocationId),
            cancellationToken);
        var result = new PurchaseOrderListDto
        {
            PurchaseOrders = searchResult.PurchaseOrders.Select(MapPurchaseOrder).ToList(),
            TotalCount = searchResult.TotalCount,
            PageNumber = searchResult.PageNumber,
            PageSize = searchResult.PageSize
        };
        return Ok(ApiResponse<PurchaseOrderListDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Get purchase order by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await _sender.Send(new GetPurchaseOrderByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(MapPurchaseOrder(purchaseOrder)));
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
    public async Task<ActionResult<ApiResponse<List<PurchaseOrderDto>>>> GetPendingApprovals(
        CancellationToken cancellationToken)
    {
        var purchaseOrders = await _sender.Send(
            new GetPendingPurchaseOrderApprovalsQuery(),
            cancellationToken);
        var pos = purchaseOrders.Select(MapPurchaseOrder).ToList();
        return Ok(ApiResponse<List<PurchaseOrderDto>>.SuccessResponse(pos));
    }

    /// <summary>
    /// Get total amount for purchase orders
    /// </summary>
    [HttpGet("total-amount")]
    public async Task<ActionResult<ApiResponse<decimal>>> GetTotalAmount(
        CancellationToken cancellationToken,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var total = await _sender.Send(
            new GetPurchaseOrderTotalAmountQuery(status, startDate, endDate),
            cancellationToken);
        return Ok(ApiResponse<decimal>.SuccessResponse(total));
    }

    /// <summary>
    /// Create a new purchase order
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "purchases.create")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Create(
        [FromBody] CreatePurchaseOrderDto dto,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedPoUserId) ? parsedPoUserId : null;

        var purchaseOrder = await _sender.Send(
            new CreatePurchaseOrderCommand(
                dto.SupplierId,
                ResolveDestination(dto.LocationType, dto.LocationId, dto.WarehouseId),
                dto.OrderDate,
                dto.ExpectedDelivery,
                dto.Status,
                dto.Notes,
                dto.Items?.Select(item => new CreatePurchaseOrderItemCommand(
                    item.VariantId,
                    item.Quantity,
                    item.UnitPrice,
                    item.Discount,
                    item.Tax,
                    item.Unit)).ToArray(),
                userId),
            cancellationToken);
        var po = MapPurchaseOrder(purchaseOrder);
        return CreatedAtAction(
            nameof(GetById),
            new { id = po.Id },
            ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order created successfully"));
    }

    private static PurchaseOrderDto MapPurchaseOrder(PurchaseOrderReadModel purchaseOrder) =>
        PurchaseDocumentDtoMapper.MapPurchaseOrder(purchaseOrder);

    private static PurchaseOrderDestination ResolveDestination(
        string? locationType,
        long? locationId,
        long? legacyWarehouseId) =>
        PurchaseOrderDestination.Resolve(locationType, locationId, legacyWarehouseId);

    /// <summary>
    /// Create purchase order and immediately receive stock (auto-GRN).
    /// </summary>
    [HttpPost("purchase-receive")]
    [Authorize(Policy = "purchases.receive")]
    public async Task<ActionResult<ApiResponse<PurchaseAndReceiveResultDto>>> PurchaseAndReceive(
        [FromBody] CreatePurchaseAndReceiveDto dto,
        CancellationToken cancellationToken)
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
            var result = await _sender.Send(
                new PurchaseAndReceiveCommand(
                    dto.SupplierId,
                    ResolveDestination(dto.LocationType, dto.LocationId, dto.WarehouseId),
                    dto.OrderDate,
                    dto.ExpectedDelivery,
                    dto.Notes,
                    dto.Items?.Select(item => new PurchaseAndReceiveItemCommand(
                        item.VariantId,
                        item.Quantity,
                        item.UnitPrice,
                        item.Discount,
                        item.Tax,
                        item.Unit)).ToArray(),
                    dto.IdempotencyKey,
                    userId.Value),
                cancellationToken);
            await _sender.Send(new FlushAuditScopeCommand(), CancellationToken.None);

            var message = result.IsDuplicateRequest
                ? "Duplicate request detected; existing purchase receive result returned"
                : "Purchase order created, auto-GRN generated, and stock updated successfully";
            var response = new PurchaseAndReceiveResultDto
            {
                PurchaseOrder = MapPurchaseOrder(result.PurchaseOrder),
                GrnId = result.GrnId,
                IsDuplicateRequest = result.IsDuplicateRequest
            };

            return Ok(ApiResponse<PurchaseAndReceiveResultDto>.SuccessResponse(response, message));
        }
        catch (Exception ex)
        {
            _auditCtx.FailScope(ex.Message);
            await _sender.Send(new FlushAuditScopeCommand(), CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Update an existing purchase order
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "purchases.edit")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Update(
        long id,
        [FromBody] UpdatePurchaseOrderDto dto,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await _sender.Send(
            new UpdatePurchaseOrderCommand(
                id,
                dto.SupplierId,
                ResolveDestination(dto.LocationType, dto.LocationId, dto.WarehouseId),
                dto.OrderDate,
                dto.ExpectedDelivery,
                dto.Notes,
                dto.Items?.Select(item => new UpdatePurchaseOrderItemCommand(
                    item.VariantId,
                    item.Quantity,
                    item.UnitPrice,
                    item.Discount,
                    item.Tax,
                    item.Unit)).ToArray()),
            cancellationToken);
        var po = MapPurchaseOrder(purchaseOrder);
        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order updated successfully"));
    }

    /// <summary>
    /// Delete a purchase order (only if draft/pending with no GRNs)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "purchases.edit")]
    public async Task<ActionResult<ApiResponse>> Delete(
        long id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeletePurchaseOrderCommand(id), cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Purchase order deleted successfully"));
    }

    /// <summary>
    /// Submit purchase order for approval (Draft → Pending)
    /// </summary>
    [HttpPost("{id}/submit")]
    [Authorize(Policy = "purchases.approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Submit(
        long id,
        CancellationToken cancellationToken)
    {
        _auditCtx.BeginScope(
            "Submit Purchase Order", AuditActionType.Submit,
            AuditModule.Purchase, "PurchaseOrder", id.ToString());

        var purchaseOrder = await _sender.Send(
            new SubmitPurchaseOrderForApprovalCommand(id),
            cancellationToken);
        var po = MapPurchaseOrder(purchaseOrder);
        await _sender.Send(new FlushAuditScopeCommand(), CancellationToken.None);

        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order submitted for approval"));
    }

    /// <summary>
    /// Approve purchase order (Pending → Approved)
    /// </summary>
    [HttpPost("{id}/approve")]
    [Authorize(Policy = "purchases.approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Approve(
        long id,
        CancellationToken cancellationToken)
    {
        _auditCtx.BeginScope(
            "Approve Purchase Order", AuditActionType.Approve,
            AuditModule.Purchase, "PurchaseOrder", id.ToString());

        var approved = await _sender.Send(
            new ApprovePurchaseOrderCommand(id),
            cancellationToken);
        var po = MapPurchaseOrder(approved);
        await _sender.Send(new FlushAuditScopeCommand(), CancellationToken.None);

        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order approved"));
    }

    /// <summary>
    /// Reject purchase order
    /// </summary>
    [HttpPost("{id}/reject")]
    [Authorize(Policy = "purchases.approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Reject(
        long id,
        [FromBody] UpdatePurchaseOrderStatusDto dto,
        CancellationToken cancellationToken)
    {
        _auditCtx.BeginScope(
            "Reject Purchase Order", AuditActionType.Reject,
            AuditModule.Purchase, "PurchaseOrder", id.ToString());

        var rejected = await _sender.Send(
            new RejectPurchaseOrderCommand(id, dto.Reason ?? "No reason provided"),
            cancellationToken);
        var po = MapPurchaseOrder(rejected);
        await _sender.Send(new FlushAuditScopeCommand(), CancellationToken.None);

        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order rejected"));
    }

    /// <summary>
    /// Cancel purchase order
    /// </summary>
    [HttpPost("{id}/cancel")]
    [Authorize(Policy = "purchases.approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> Cancel(
        long id,
        [FromBody] UpdatePurchaseOrderStatusDto dto,
        CancellationToken cancellationToken)
    {
        _auditCtx.BeginScope(
            "Cancel Purchase Order", AuditActionType.Cancel,
            AuditModule.Purchase, "PurchaseOrder", id.ToString());

        var cancelled = await _sender.Send(
            new CancelPurchaseOrderCommand(id, dto.Reason ?? "No reason provided"),
            cancellationToken);
        var po = MapPurchaseOrder(cancelled);
        await _sender.Send(new FlushAuditScopeCommand(), CancellationToken.None);

        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order cancelled"));
    }

    /// <summary>
    /// Send purchase order back for correction (Pending → Sent Back)
    /// </summary>
    [HttpPost("{id}/send-back")]
    [Authorize(Policy = "purchases.approve")]
    public async Task<ActionResult<ApiResponse<PurchaseOrderDto>>> SendBack(
        long id,
        [FromBody] UpdatePurchaseOrderStatusDto dto,
        CancellationToken cancellationToken)
    {
        _auditCtx.BeginScope(
            "Send Back Purchase Order", AuditActionType.Update,
            AuditModule.Purchase, "PurchaseOrder", id.ToString());

        var sentBack = await _sender.Send(
            new SendBackPurchaseOrderCommand(id, dto.Reason ?? "No reason provided"),
            cancellationToken);
        var po = MapPurchaseOrder(sentBack);
        await _sender.Send(new FlushAuditScopeCommand(), CancellationToken.None);

        return Ok(ApiResponse<PurchaseOrderDto>.SuccessResponse(po, "Purchase order sent back for correction"));
    }
}
