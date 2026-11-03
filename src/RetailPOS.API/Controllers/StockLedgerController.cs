// =====================================================================
// NEW — Stock Ledger query controller
// Read-only: ledger entries are written automatically by inventory-mutating
// flows (GRN, Sale, Transfer, Adjustment). There is no POST/PUT/DELETE.
// =====================================================================
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.DTOs.StockLedger;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Application.Inventory.StockLedger;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-ledger")]
[Authorize(Policy = "inventory.view")]
public class StockLedgerController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IUserOutletAccessService _outletAccess;

    public StockLedgerController(ISender sender, IUserOutletAccessService outletAccess)
    {
        _sender = sender;
        _outletAccess = outletAccess;
    }

    /// <summary>
    /// Get a single ledger entry by ID.
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<StockLedgerDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var entry = await _sender.Send(new GetStockLedgerEntryQuery(id), cancellationToken);
        if (entry == null)
            return NotFound(ApiResponse<StockLedgerDto>.ErrorResponse($"Stock ledger entry {id} not found"));

        return Ok(ApiResponse<StockLedgerDto>.SuccessResponse(MapToDto(entry)));
    }

    /// <summary>
    /// Full movement timeline for a specific variant at a specific location (oldest first).
    /// Useful for "stock card" views.
    /// </summary>
    [HttpGet("variant/{variantId:long}/location/{locationId:long}")]
    public async Task<ActionResult<ApiResponse<List<StockLedgerDto>>>> GetTimeline(
        long variantId,
        long locationId,
        [FromQuery] string locationType = "outlet",
        CancellationToken cancellationToken = default)
    {
        var authorizedLocation = await _outletAccess.ResolveAndAuthorizeLocationAsync(
            locationId,
            locationType);
        var entries = await _sender.Send(
            new GetStockLedgerTimelineQuery(
                variantId,
                authorizedLocation.LocationId ?? locationId,
                authorizedLocation.LocationType ?? locationType),
            cancellationToken);

        return Ok(ApiResponse<List<StockLedgerDto>>.SuccessResponse(
            entries.Select(MapToDto).ToList()));
    }

    /// <summary>
    /// All ledger rows produced by one source document,
    /// e.g. GET /api/stock-ledger/reference/sale/42
    /// </summary>
    [HttpGet("reference/{referenceType}/{referenceId:long}")]
    public async Task<ActionResult<ApiResponse<List<StockLedgerDto>>>> GetByReference(
        string referenceType,
        long referenceId,
        CancellationToken cancellationToken)
    {
        var entries = await _sender.Send(
            new GetStockLedgerByReferenceQuery(referenceType, referenceId),
            cancellationToken);
        return Ok(ApiResponse<List<StockLedgerDto>>.SuccessResponse(
            entries.Select(MapToDto).ToList()));
    }

    /// <summary>
    /// Paginated search with optional filters.
    /// Supports filtering by variant, location, transaction type, reference document and date range.
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockLedgerListDto>>> Search(
        [FromBody] StockLedgerSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        if (searchDto.LocationId.HasValue || !string.IsNullOrWhiteSpace(searchDto.LocationType))
        {
            if (searchDto.LocationId.HasValue && string.IsNullOrWhiteSpace(searchDto.LocationType))
            {
                var authorized = await _outletAccess.GetAuthorizedOutletsAsync();
                var outletMatch = authorized.Outlets
                    .Any(location => location.Id == searchDto.LocationId.Value);
                var warehouseMatch = authorized.Warehouses
                    .Any(location => location.Id == searchDto.LocationId.Value);
                if (!outletMatch && !warehouseMatch)
                {
                    throw new UnauthorizedAccessException(
                        $"You are not authorized to view data for location #{searchDto.LocationId.Value}.");
                }

                searchDto.LocationType = outletMatch ? "outlet" : "warehouse";
            }
            else
            {
                var authorizedLocation = await _outletAccess.ResolveAndAuthorizeLocationAsync(
                    searchDto.LocationId,
                    searchDto.LocationType);
                searchDto.LocationId = authorizedLocation.LocationId;
                searchDto.LocationType = authorizedLocation.LocationType;
            }
        }

        var result = await _sender.Send(
            new SearchStockLedgerQuery(new StockLedgerSearchFilter(
                searchDto.VariantId,
                searchDto.LocationId,
                searchDto.LocationType,
                searchDto.TransactionType,
                searchDto.ReferenceType,
                searchDto.ReferenceId,
                searchDto.StartDate,
                searchDto.EndDate,
                searchDto.PageNumber,
                searchDto.PageSize)),
            cancellationToken);
        return Ok(ApiResponse<StockLedgerListDto>.SuccessResponse(new StockLedgerListDto
        {
            Entries = result.Entries.Select(MapToDto).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages,
            HasPreviousPage = result.HasPreviousPage,
            HasNextPage = result.HasNextPage
        }));
    }

    private static StockLedgerDto MapToDto(StockLedgerReadModel entry) => new()
    {
        Id = entry.Id,
        VariantId = entry.VariantId,
        VariantSku = entry.VariantSku,
        ProductName = entry.ProductName,
        LocationId = entry.LocationId,
        LocationType = entry.LocationType,
        LocationName = entry.LocationName,
        TransactionType = entry.TransactionType,
        QtyIn = entry.QtyIn,
        QtyOut = entry.QtyOut,
        BalanceAfter = entry.BalanceAfter,
        UnitCost = entry.UnitCost,
        MovementValue = entry.MovementValue,
        IsCostValuationReliable = entry.IsCostValuationReliable,
        BusinessDate = entry.BusinessDate,
        MovementSequence = entry.MovementSequence,
        ReversalOfMovementId = entry.ReversalOfMovementId,
        ReferenceType = entry.ReferenceType,
        ReferenceId = entry.ReferenceId,
        Remarks = entry.Remarks,
        CreatedBy = entry.CreatedBy,
        CreatedByName = entry.CreatedByName,
        CreatedAt = entry.CreatedAt
    };
}
