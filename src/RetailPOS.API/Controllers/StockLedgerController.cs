// =====================================================================
// NEW — Stock Ledger query controller
// Read-only: ledger entries are written automatically by inventory-mutating
// flows (GRN, Sale, Transfer, Adjustment). There is no POST/PUT/DELETE.
// =====================================================================
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.StockLedger;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-ledger")]
[Authorize(Policy = "inventory.view")]
public class StockLedgerController : ControllerBase
{
    private readonly IStockLedgerService _ledgerService;

    public StockLedgerController(IStockLedgerService ledgerService)
    {
        _ledgerService = ledgerService;
    }

    /// <summary>
    /// Get a single ledger entry by ID.
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<StockLedgerDto>>> GetById(long id)
    {
        var entry = await _ledgerService.GetByIdAsync(id);
        if (entry == null)
            return NotFound(ApiResponse<StockLedgerDto>.ErrorResponse($"Stock ledger entry {id} not found"));

        return Ok(ApiResponse<StockLedgerDto>.SuccessResponse(entry));
    }

    /// <summary>
    /// Full movement timeline for a specific variant at a specific location (oldest first).
    /// Useful for "stock card" views.
    /// </summary>
    [HttpGet("variant/{variantId:long}/location/{locationId:long}")]
    public async Task<ActionResult<ApiResponse<List<StockLedgerDto>>>> GetTimeline(
        long variantId,
        long locationId,
        [FromQuery] string locationType = "outlet")
    {
        var entries = await _ledgerService.GetByVariantAndLocationAsync(
            variantId, locationId, locationType);

        return Ok(ApiResponse<List<StockLedgerDto>>.SuccessResponse(entries));
    }

    /// <summary>
    /// All ledger rows produced by one source document,
    /// e.g. GET /api/stock-ledger/reference/sale/42
    /// </summary>
    [HttpGet("reference/{referenceType}/{referenceId:long}")]
    public async Task<ActionResult<ApiResponse<List<StockLedgerDto>>>> GetByReference(
        string referenceType,
        long referenceId)
    {
        var entries = await _ledgerService.GetByReferenceAsync(referenceType, referenceId);
        return Ok(ApiResponse<List<StockLedgerDto>>.SuccessResponse(entries));
    }

    /// <summary>
    /// Paginated search with optional filters.
    /// Supports filtering by variant, location, transaction type, reference document and date range.
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockLedgerListDto>>> Search(
        [FromBody] StockLedgerSearchDto searchDto)
    {
        var result = await _ledgerService.SearchAsync(searchDto);
        return Ok(ApiResponse<StockLedgerListDto>.SuccessResponse(result));
    }
}
