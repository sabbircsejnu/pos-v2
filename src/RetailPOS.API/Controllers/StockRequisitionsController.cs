using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.StockRequisition;
using RetailPOS.API.DTOs.StockTransfer;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-requisitions")]
[Authorize(Policy = "stock_requisitions.view")]
public class StockRequisitionsController : ControllerBase
{
    private readonly IStockRequisitionService _requisitionService;
    private readonly IUserOutletAccessService _outletAccess;

    public StockRequisitionsController(
        IStockRequisitionService requisitionService,
        IUserOutletAccessService outletAccess)
    {
        _requisitionService = requisitionService;
        _outletAccess = outletAccess;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<StockRequisitionDto>>>> GetAll(
        [FromQuery] string? status = null,
        [FromQuery] long? requestingLocationId = null,
        [FromQuery] long? sourceLocationId = null)
    {
        await EnforceLocationFiltersAsync(requestingLocationId, sourceLocationId);

        var requisitions = await _requisitionService.GetAllAsync(status, requestingLocationId, sourceLocationId);
        return Ok(ApiResponse<List<StockRequisitionDto>>.SuccessResponse(requisitions));
    }

    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockRequisitionListDto>>> Search([FromBody] StockRequisitionSearchDto dto)
    {
        await EnforceLocationFiltersAsync(dto.RequestingLocationId, dto.SourceLocationId);

        var result = await _requisitionService.SearchAsync(dto);
        return Ok(ApiResponse<StockRequisitionListDto>.SuccessResponse(result));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> GetById(long id)
    {
        var requisition = await _requisitionService.GetByIdAsync(id);
        await EnsureRequisitionAuthorizedAsync(requisition);
        return Ok(ApiResponse<StockRequisitionDto>.SuccessResponse(requisition));
    }

    [HttpPost]
    [Authorize(Policy = "stock_requisitions.create")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> Create([FromBody] CreateStockRequisitionDto dto)
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
        var requisition = await _requisitionService.CreateAsync(dto, requestedBy);
        return CreatedAtAction(nameof(GetById), new { id = requisition.Id }, ApiResponse<StockRequisitionDto>.SuccessResponse(requisition, "Stock requisition created successfully"));
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "stock_requisitions.edit")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> Update(long id, [FromBody] CreateStockRequisitionDto dto)
    {
        var existing = await _requisitionService.GetByIdAsync(id);
        await EnsureRequisitionAuthorizedAsync(existing);

        var (requestingId, requestingType) = await _outletAccess.EnforceWriteLocationAsync(dto.RequestingLocationId, dto.RequestingLocationType);
        dto.RequestingLocationId = requestingId;
        dto.RequestingLocationType = requestingType;

        var (sourceId, sourceType) = await _outletAccess.ResolveAndAuthorizeLocationAsync(dto.SourceLocationId, dto.SourceLocationType);
        if (!sourceId.HasValue || string.IsNullOrWhiteSpace(sourceType))
            throw new InvalidOperationException("Source location is required.");

        dto.SourceLocationId = sourceId.Value;
        dto.SourceLocationType = sourceType;

        var updatedBy = GetCurrentUserIdOrThrow();
        var requisition = await _requisitionService.UpdateAsync(id, dto, updatedBy);
        return Ok(ApiResponse<StockRequisitionDto>.SuccessResponse(requisition, "Stock requisition updated successfully"));
    }

    [HttpPost("{id}/submit")]
    [Authorize(Policy = "stock_requisitions.create")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> Submit(long id)
    {
        var existing = await _requisitionService.GetByIdAsync(id);
        await EnsureRequisitionAuthorizedAsync(existing);

        var submittedBy = GetCurrentUserIdOrThrow();
        var requisition = await _requisitionService.SubmitAsync(id, submittedBy);
        return Ok(ApiResponse<StockRequisitionDto>.SuccessResponse(requisition, "Stock requisition submitted"));
    }

    [HttpPost("{id}/approve")]
    [Authorize(Policy = "stock_requisitions.approve")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> Approve(long id)
    {
        var existing = await _requisitionService.GetByIdAsync(id);
        await EnsureRequisitionAuthorizedAsync(existing);

        var approvedBy = GetCurrentUserIdOrThrow();
        var requisition = await _requisitionService.ApproveAsync(id, approvedBy);
        return Ok(ApiResponse<StockRequisitionDto>.SuccessResponse(requisition, "Stock requisition approved"));
    }

    [HttpPost("{id}/reject")]
    [Authorize(Policy = "stock_requisitions.reject")]
    public async Task<ActionResult<ApiResponse<StockRequisitionDto>>> Reject(long id, [FromBody] RequisitionStatusUpdateDto dto)
    {
        var existing = await _requisitionService.GetByIdAsync(id);
        await EnsureRequisitionAuthorizedAsync(existing);

        var rejectedBy = GetCurrentUserIdOrThrow();
        var requisition = await _requisitionService.RejectAsync(id, rejectedBy, dto.Reason);
        return Ok(ApiResponse<StockRequisitionDto>.SuccessResponse(requisition, "Stock requisition rejected"));
    }

    [HttpPost("{id}/convert-to-transfer")]
    [Authorize(Policy = "stock_requisitions.convert_to_transfer")]
    public async Task<ActionResult<ApiResponse<StockTransferDto>>> ConvertToTransfer(long id)
    {
        var existing = await _requisitionService.GetByIdAsync(id);
        await EnsureRequisitionAuthorizedAsync(existing);

        var userId = GetCurrentUserIdOrThrow();
        var transfer = await _requisitionService.ConvertToTransferAsync(id, userId);
        return Ok(ApiResponse<StockTransferDto>.SuccessResponse(transfer, "Transfer created from requisition"));
    }

    private async Task EnforceLocationFiltersAsync(long? requestingLocationId, long? sourceLocationId)
    {
        if (requestingLocationId.HasValue)
            await _outletAccess.ResolveAndAuthorizeLocationAsync(requestingLocationId, null);
        if (sourceLocationId.HasValue)
            await _outletAccess.ResolveAndAuthorizeLocationAsync(sourceLocationId, null);
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
