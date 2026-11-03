using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.Grns.Commands;
using RetailPOS.Application.Grns.Queries;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.API.DTOs.Grn;
using RetailPOS.API.Models;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/grns")]
[Authorize(Policy = "grn.view")]
public class GrnsController : ControllerBase
{
    private readonly ISender _sender;

    public GrnsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Get all GRNs with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<GrnDto>>>> GetAll(
        [FromQuery] long? poId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var grns = await _sender.Send(new GetGrnsQuery(poId, status), cancellationToken);
        return Ok(ApiResponse<List<GrnDto>>.SuccessResponse(grns.Select(MapGrn).ToList()));
    }

    /// <summary>
    /// Search GRNs with pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<GrnListDto>>> Search(
        [FromBody] GrnSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SearchGrnsQuery(
                searchDto.PoId,
                searchDto.Status,
                searchDto.StartDate,
                searchDto.EndDate,
                searchDto.PageNumber,
                searchDto.PageSize),
            cancellationToken);
        return Ok(ApiResponse<GrnListDto>.SuccessResponse(new GrnListDto
        {
            Grns = result.Grns.Select(MapGrn).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        }));
    }

    /// <summary>
    /// Get GRN by ID with full details
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<GrnDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var grn = await _sender.Send(new GetGrnByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<GrnDto>.SuccessResponse(MapGrn(grn)));
    }

    /// <summary>
    /// Get purchase orders awaiting receipt (status = "approved")
    /// </summary>
    [HttpGet("pending-pos")]
    public async Task<ActionResult<ApiResponse<List<PurchaseOrderForGrnDto>>>> GetPendingPos(
        CancellationToken cancellationToken)
    {
        var purchaseOrders = await _sender.Send(
            new GetPendingGrnPurchaseOrdersQuery(),
            cancellationToken);
        var response = purchaseOrders.Select(order => new PurchaseOrderForGrnDto
        {
            Id = order.Id,
            OrderNumber = $"PO-{order.Id:D6}",
            SupplierId = order.SupplierId,
            SupplierName = order.SupplierName,
            WarehouseId = order.WarehouseId,
            WarehouseName = order.WarehouseName,
            OutletId = order.OutletId,
            OutletName = order.OutletName,
            LocationType = order.LocationType,
            LocationId = order.LocationId,
            LocationName = order.LocationName,
            OrderDate = order.OrderDate,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            Items = order.Items.Select(item => new PurchaseOrderItemForGrnDto
            {
                Id = item.Id,
                VariantId = item.VariantId,
                ProductName = item.ProductName,
                ProductCode = item.ProductCode,
                VariantName = item.VariantName,
                VariantAttributes = item.VariantAttributes,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                PrimaryImageThumb = item.PrimaryImageThumb
            }).ToList()
        }).ToList();
        return Ok(ApiResponse<List<PurchaseOrderForGrnDto>>.SuccessResponse(response));
    }

    /// <summary>
    /// Get variance report (ordered vs received quantities) for a single GRN
    /// </summary>
    [HttpGet("{id}/variance")]
    public async Task<ActionResult<ApiResponse<GrnVarianceDto>>> GetVariance(
        long id,
        CancellationToken cancellationToken)
    {
        var variance = await _sender.Send(new GetGrnVarianceQuery(id), cancellationToken);
        return Ok(ApiResponse<GrnVarianceDto>.SuccessResponse(new GrnVarianceDto
        {
            GrnId = variance.GrnId,
            PoId = variance.PurchaseOrderId,
            PoOrderNumber = $"PO-{variance.PurchaseOrderId:D6}",
            GrnStatus = variance.Status,
            Items = variance.Items.Select(item => new GrnVarianceItemDto
            {
                PoItemId = item.PurchaseOrderItemId,
                VariantId = item.VariantId,
                ProductName = item.ProductName,
                VariantName = item.VariantName,
                OrderedQty = item.OrderedQuantity,
                ReceivedQty = item.ReceivedQuantity
            }).ToList()
        }));
    }

    /// <summary>
    /// Get cumulative variance for an entire PO across all its GRNs
    /// (shows remaining quantities outstanding per line item)
    /// </summary>
    [HttpGet("po/{poId}/variance")]                  // NEW
    public async Task<ActionResult<ApiResponse<GrnPoVarianceDto>>> GetPoVariance(
        long poId,
        CancellationToken cancellationToken)
    {
        var variance = await _sender.Send(new GetGrnPoVarianceQuery(poId), cancellationToken);
        return Ok(ApiResponse<GrnPoVarianceDto>.SuccessResponse(new GrnPoVarianceDto
        {
            PoId = variance.PurchaseOrderId,
            PoOrderNumber = $"PO-{variance.PurchaseOrderId:D6}",
            PoStatus = variance.PurchaseOrderStatus,
            TotalGrns = variance.TotalGrns,
            CompletedGrns = variance.CompletedGrns,
            Items = variance.Items.Select(item => new GrnPoVarianceItemDto
            {
                PoItemId = item.PurchaseOrderItemId,
                VariantId = item.VariantId,
                ProductName = item.ProductName,
                VariantName = item.VariantName,
                OrderedQty = item.OrderedQuantity,
                TotalReceivedQty = item.TotalReceivedQuantity,
                UnitPrice = item.UnitPrice,
                TotalReceivedCost = item.TotalReceivedCost
            }).ToList()
        }));
    }

    /// <summary>
    /// Create a new GRN from an approved PO
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "grn.create")]
    public async Task<ActionResult<ApiResponse<GrnDto>>> Create(
        [FromBody] CreateGrnDto dto,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedId) ? parsedId : null;

        var result = await _sender.Send(
            new CreateGrnCommand(
                dto.PoId,
                dto.ReceivedDate,
                dto.Notes,
                dto.SupplierInvoiceNumber,
                dto.SupplierInvoiceDate,
                dto.Items.Select(item => new CreateGrnItemCommand(
                    item.PoItemId,
                    item.ReceivedQty,
                    item.UnitCost,
                    item.Notes)).ToArray(),
                userId),
            cancellationToken);
        var grn = MapGrn(result);
        return CreatedAtAction(
            nameof(GetById),
            new { id = grn.Id },
            ApiResponse<GrnDto>.SuccessResponse(grn, "GRN created successfully"));
    }

    /// <summary>
    /// Complete a GRN: update inventory stock and PO status
    /// </summary>
    [HttpPost("{id}/complete")]
    [Authorize(Policy = "grn.receive")]
    public async Task<ActionResult<ApiResponse<GrnDto>>> Complete(
        long id,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(ApiResponse<GrnDto>.ErrorResponse("User identity not found"));
        }

        var grn = await _sender.Send(new CompleteGrnCommand(id, userId), cancellationToken);
        return Ok(ApiResponse<GrnDto>.SuccessResponse(
            MapGrn(grn),
            "GRN completed and stock updated successfully"));
    }

    private static GrnDto MapGrn(GrnReadModel grn) =>
        RetailPOS.API.Documents.Pdf.PurchaseDocumentDtoMapper.MapGrn(grn);
}
