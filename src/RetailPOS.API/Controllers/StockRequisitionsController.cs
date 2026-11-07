using RetailPOS.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.DTOs.StockRequisition;
using RetailPOS.API.DTOs.StockTransfer;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.Commands;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Application.Inventory.StockRequisitions;
using RetailPOS.Application.Inventory.StockTransfers;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-requisitions")]
[Authorize(Policy = "stock_requisitions.view")]
public class StockRequisitionsController : ControllerBase
{
    private readonly IUserOutletAccessService _outletAccess;
    private readonly ISender _sender;
    private readonly IVariantDisplayFormatterService _variantDisplayFormatter;

    public StockRequisitionsController(
        IUserOutletAccessService outletAccess,
        ISender sender,
        IVariantDisplayFormatterService variantDisplayFormatter)
    {
        _outletAccess = outletAccess;
        _sender = sender;
        _variantDisplayFormatter = variantDisplayFormatter;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<StockRequisitionDto>>>> GetAll(
        [FromQuery] string? status = null,
        [FromQuery] long? requestingLocationId = null,
        [FromQuery] long? sourceLocationId = null,
        CancellationToken cancellationToken = default)
    {
        await EnforceLocationFiltersAsync(requestingLocationId, sourceLocationId);

        var requisitions = await _sender.Send(
            new GetStockRequisitionsQuery(new StockRequisitionFilter(
                status,
                requestingLocationId,
                null,
                sourceLocationId,
                null,
                1,
                int.MaxValue)),
            cancellationToken);
        var rows = new List<StockRequisitionDto>(requisitions.Count);
        foreach (var requisition in requisitions)
            rows.Add(await MapRequisitionAsync(requisition));

        return Ok(ApiResponse<List<StockRequisitionDto>>.SuccessResponse(rows));
    }

    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockRequisitionListDto>>> Search(
        [FromBody] StockRequisitionSearchDto dto,
        CancellationToken cancellationToken)
    {
        await EnforceLocationFiltersAsync(dto.RequestingLocationId, dto.SourceLocationId);

        var result = await _sender.Send(
            new SearchStockRequisitionsQuery(new StockRequisitionFilter(
                dto.Status,
                dto.RequestingLocationId,
                dto.RequestingLocationType,
                dto.SourceLocationId,
                dto.SourceLocationType,
                dto.PageNumber,
                dto.PageSize)),
            cancellationToken);
        var rows = new List<StockRequisitionDto>(result.StockRequisitions.Count);
        foreach (var requisition in result.StockRequisitions)
            rows.Add(await MapRequisitionAsync(requisition));

        return Ok(ApiResponse<StockRequisitionListDto>.SuccessResponse(new StockRequisitionListDto
        {
            StockRequisitions = rows,
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        }));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var requisition = await _sender.Send(new GetStockRequisitionByIdQuery(id), cancellationToken);
        var dto = await MapRequisitionAsync(requisition);
        await EnsureRequisitionAuthorizedAsync(dto);
        return Ok(ApiResponse<StockRequisitionDto>.SuccessResponse(dto));
    }

    [HttpPost]
    [Authorize(Policy = "stock_requisitions.create")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> Create(
        [FromBody] CreateStockRequisitionDto dto,
        CancellationToken cancellationToken)
    {
        var (requestingId, requestingType) = await _outletAccess.EnforceWriteLocationAsync(dto.RequestingLocationId, dto.RequestingLocationType);
        dto.RequestingLocationId = requestingId;
        dto.RequestingLocationType = requestingType;

        var (sourceId, sourceType) = await _outletAccess.ResolveAndAuthorizeLocationAsync(dto.SourceLocationId, dto.SourceLocationType);
        if (!sourceId.HasValue || string.IsNullOrWhiteSpace(sourceType))
            throw new InvalidOperationException("Source location is required.");

        dto.SourceLocationId = sourceId.Value;
        dto.SourceLocationType = sourceType;

        var requestedBy = GetCurrentUserIdOrThrow();
        var requisition = await _sender.Send(
            new CreateStockRequisitionCommand(ToWriteInput(dto), requestedBy),
            cancellationToken);
        var response = await MapRequisitionAsync(requisition);
        return CreatedAtAction(
            nameof(GetById),
            new { id = requisition.Id },
            ApiResponse<StockRequisitionDto>.SuccessResponse(
                response,
                "Stock requisition created successfully"));
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "stock_requisitions.edit")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> Update(
        long id,
        [FromBody] CreateStockRequisitionDto dto,
        CancellationToken cancellationToken)
    {
        await EnsureRequisitionAuthorizedAsync(id, cancellationToken);

        var (requestingId, requestingType) = await _outletAccess.EnforceWriteLocationAsync(dto.RequestingLocationId, dto.RequestingLocationType);
        dto.RequestingLocationId = requestingId;
        dto.RequestingLocationType = requestingType;

        var (sourceId, sourceType) = await _outletAccess.ResolveAndAuthorizeLocationAsync(dto.SourceLocationId, dto.SourceLocationType);
        if (!sourceId.HasValue || string.IsNullOrWhiteSpace(sourceType))
            throw new InvalidOperationException("Source location is required.");

        dto.SourceLocationId = sourceId.Value;
        dto.SourceLocationType = sourceType;

        var updatedBy = GetCurrentUserIdOrThrow();
        var requisition = await _sender.Send(
            new UpdateStockRequisitionCommand(id, ToWriteInput(dto), updatedBy),
            cancellationToken);
        return Ok(ApiResponse<StockRequisitionDto>.SuccessResponse(
            await MapRequisitionAsync(requisition),
            "Stock requisition updated successfully"));
    }

    [HttpPost("{id}/submit")]
    [Authorize(Policy = "stock_requisitions.create")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> Submit(
        long id,
        CancellationToken cancellationToken)
    {
        await EnsureRequisitionAuthorizedAsync(id, cancellationToken);

        var submittedBy = GetCurrentUserIdOrThrow();
        var requisition = await _sender.Send(
            new SubmitStockRequisitionCommand(id, submittedBy),
            cancellationToken);
        return Ok(ApiResponse<StockRequisitionDto>.SuccessResponse(
            await MapRequisitionAsync(requisition),
            "Stock requisition submitted"));
    }

    [HttpPost("{id}/approve")]
    [Authorize(Policy = "stock_requisitions.approve")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> Approve(
        long id,
        CancellationToken cancellationToken)
    {
        await EnsureRequisitionAuthorizedAsync(id, cancellationToken);

        var approvedBy = GetCurrentUserIdOrThrow();
        var requisition = await _sender.Send(
            new ApproveStockRequisitionCommand(id, approvedBy),
            cancellationToken);
        return Ok(ApiResponse<StockRequisitionDto>.SuccessResponse(
            await MapRequisitionAsync(requisition),
            "Stock requisition approved"));
    }

    [HttpPost("{id}/reject")]
    [Authorize(Policy = "stock_requisitions.reject")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> Reject(
        long id,
        [FromBody] RequisitionStatusUpdateDto dto,
        CancellationToken cancellationToken)
    {
        await EnsureRequisitionAuthorizedAsync(id, cancellationToken);

        var rejectedBy = GetCurrentUserIdOrThrow();
        var requisition = await _sender.Send(
            new RejectStockRequisitionCommand(id, rejectedBy, dto.Reason),
            cancellationToken);
        return Ok(ApiResponse<StockRequisitionDto>.SuccessResponse(
            await MapRequisitionAsync(requisition),
            "Stock requisition rejected"));
    }

    [HttpPost("{id}/convert-to-transfer")]
    [Authorize(Policy = "stock_requisitions.convert_to_transfer")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> ConvertToTransfer(
        long id,
        CancellationToken cancellationToken)
    {
        await EnsureRequisitionAuthorizedAsync(id, cancellationToken);

        var userId = GetCurrentUserIdOrThrow();
        var transfer = await _sender.Send(
            new ConvertStockRequisitionToTransferCommand(id, userId),
            cancellationToken);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(
            await MapTransferAsync(transfer),
            "Transfer created from requisition"));
    }

    private async Task<StockTransferDto> MapTransferAsync(StockTransferReadModel transfer)
    {
        var items = new List<StockTransferItemDto>(transfer.Items.Count);
        foreach (var item in transfer.Items)
        {
            items.Add(new StockTransferItemDto
            {
                Id = item.Id,
                VariantId = item.VariantId,
                ProductCode = item.ProductCode,
                VariantSku = item.VariantSku,
                ProductName = item.ProductName,
                VariantAttributes = item.VariantAttributes,
                VariantDisplay = await _variantDisplayFormatter.FormatAsync(
                    item.VariantAttributes,
                    HttpContext.RequestAborted),
                PrimaryImageThumb = item.PrimaryImageThumb,
                Quantity = item.Quantity,
                RequestedQuantity = item.RequestedQuantity,
                TransferQuantity = item.TransferQuantity,
                AcceptedQuantity = item.AcceptedQuantity,
                RejectedQuantity = item.RejectedQuantity,
                // U-13: cost data, shown only with products.view_cost.
                UnitCost = User.CanViewCost() ? item.UnitCost : null,
                Remarks = item.Remarks
            });
        }

        return new StockTransferDto
        {
            Id = transfer.Id,
            TransferNo = transfer.TransferNo,
            TransferType = transfer.TransferType,
            RelatedRequisitionId = transfer.RelatedRequisitionId,
            FromLocationId = transfer.FromLocationId,
            FromLocationType = transfer.FromLocationType,
            FromLocationName = transfer.FromLocationName,
            ToLocationId = transfer.ToLocationId,
            ToLocationType = transfer.ToLocationType,
            ToLocationName = transfer.ToLocationName,
            TransferDate = transfer.TransferDate,
            Status = transfer.Status,
            Notes = transfer.Notes,
            ApprovedBy = transfer.ApprovedBy,
            ApproverName = transfer.ApproverName,
            ApprovedAt = transfer.ApprovedAt,
            SubmittedBy = transfer.SubmittedBy,
            SubmittedAt = transfer.SubmittedAt,
            DispatchedBy = transfer.DispatchedBy,
            DispatchedAt = transfer.DispatchedAt,
            ReceivedBy = transfer.ReceivedBy,
            ReceivedAt = transfer.ReceivedAt,
            RejectedBy = transfer.RejectedBy,
            RejectedAt = transfer.RejectedAt,
            CancelledBy = transfer.CancelledBy,
            CancelledAt = transfer.CancelledAt,
            UpdatedBy = transfer.UpdatedBy,
            UpdatedAt = transfer.UpdatedAt,
            CreatedBy = transfer.CreatedBy,
            CreatorName = transfer.CreatorName,
            CreatedAt = transfer.CreatedAt,
            Items = items
        };
    }

    private async Task<StockRequisitionDto> MapRequisitionAsync(StockRequisitionReadModel requisition)
    {
        var lines = new List<StockRequisitionLineDto>(requisition.Lines.Count);
        foreach (var line in requisition.Lines)
        {
            lines.Add(new StockRequisitionLineDto
            {
                Id = line.Id,
                VariantId = line.VariantId,
                ProductCode = line.ProductCode,
                VariantSku = line.VariantSku,
                ProductName = line.ProductName,
                VariantAttributes = line.VariantAttributes,
                VariantDisplay = await _variantDisplayFormatter.FormatAsync(
                    line.VariantAttributes,
                    HttpContext.RequestAborted),
                PrimaryImageThumb = line.PrimaryImageThumb,
                RequestedQuantity = line.RequestedQuantity,
                FulfilledQuantity = line.FulfilledQuantity,
                Remarks = line.Remarks
            });
        }

        return new StockRequisitionDto
        {
            Id = requisition.Id,
            RequisitionNo = requisition.RequisitionNo,
            RequestingLocationId = requisition.RequestingLocationId,
            RequestingLocationType = requisition.RequestingLocationType,
            RequestingLocationName = requisition.RequestingLocationName,
            SourceLocationId = requisition.SourceLocationId,
            SourceLocationType = requisition.SourceLocationType,
            SourceLocationName = requisition.SourceLocationName,
            RequestedBy = requisition.RequestedBy,
            RequesterName = requisition.RequesterName,
            RequestDate = requisition.RequestDate,
            Status = requisition.Status,
            Notes = requisition.Notes,
            CreatedAt = requisition.CreatedAt,
            UpdatedAt = requisition.UpdatedAt,
            UpdatedBy = requisition.UpdatedBy,
            SubmittedAt = requisition.SubmittedAt,
            SubmittedBy = requisition.SubmittedBy,
            ApprovedAt = requisition.ApprovedAt,
            ApprovedBy = requisition.ApprovedBy,
            RejectedAt = requisition.RejectedAt,
            RejectedBy = requisition.RejectedBy,
            RejectionReason = requisition.RejectionReason,
            ClosedAt = requisition.ClosedAt,
            ClosedBy = requisition.ClosedBy,
            Lines = lines
        };
    }

    private static StockRequisitionWriteInput ToWriteInput(CreateStockRequisitionDto dto) =>
        new(
            dto.RequestingLocationId,
            dto.RequestingLocationType,
            dto.SourceLocationId,
            dto.SourceLocationType,
            dto.Notes,
            dto.Lines?.Select(line => new StockRequisitionWriteLineInput(
                line.VariantId,
                line.RequestedQuantity,
                line.Remarks)).ToArray() ?? []);

    private async Task EnforceLocationFiltersAsync(long? requestingLocationId, long? sourceLocationId)
    {
        if (requestingLocationId.HasValue)
            await _outletAccess.ResolveAndAuthorizeLocationAsync(requestingLocationId, null);
        if (sourceLocationId.HasValue)
            await _outletAccess.ResolveAndAuthorizeLocationAsync(sourceLocationId, null);
    }

    private async Task EnsureRequisitionAuthorizedAsync(long id, CancellationToken cancellationToken)
    {
        var requisition = await _sender.Send(new GetStockRequisitionByIdQuery(id), cancellationToken);
        await EnsureRequisitionAuthorizedAsync(await MapRequisitionAsync(requisition));
    }

    private async Task EnsureRequisitionAuthorizedAsync(StockRequisitionDto requisition)
    {
        var auth = await _outletAccess.GetAuthorizedOutletsAsync();
        if (auth.IsGlobalAccess) return;

        var allowedOutletIds = auth.Outlets.Select(o => o.Id).ToHashSet();
        var allowedWarehouseIds = auth.Warehouses.Select(w => w.Id).ToHashSet();

        var requesterMatch = requisition.RequestingLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
            ? allowedOutletIds.Contains(requisition.RequestingLocationId)
            : requisition.RequestingLocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase)
                && allowedWarehouseIds.Contains(requisition.RequestingLocationId);

        var sourceMatch = requisition.SourceLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
            ? allowedOutletIds.Contains(requisition.SourceLocationId)
            : requisition.SourceLocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase)
                && allowedWarehouseIds.Contains(requisition.SourceLocationId);

        if (!requesterMatch && !sourceMatch)
            throw new UnauthorizedAccessException("You are not authorized to view or modify this stock requisition.");
    }

    private long GetCurrentUserIdOrThrow()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!long.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("User identity not found.");

        return userId;
    }
}
