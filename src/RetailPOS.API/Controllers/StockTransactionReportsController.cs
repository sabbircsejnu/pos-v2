using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

/// <summary>
/// Stock transaction (a.k.a. "stock card") report — full movement history
/// for a product (all variants) or a single variant, sourced from the unified StockLedger.
/// Outlet/warehouse access is enforced server-side: the requested outletId is validated
/// against the current user's authorized set, and falls back to their default outlet.
/// </summary>
[ApiController]
[Route("api/reports/stock-transactions")]
[Authorize(Policy = "reports.inventory")]
public class StockTransactionReportsController : ControllerBase
{
    private readonly IInventoryReportService _inventoryReportService;
    private readonly IUserOutletAccessService _outletAccess;

    public StockTransactionReportsController(
        IInventoryReportService inventoryReportService,
        IUserOutletAccessService outletAccess)
    {
        _inventoryReportService = inventoryReportService;
        _outletAccess = outletAccess;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<StockTransactionReportDto>>> Get(
        [FromQuery] long productId,
        [FromQuery] long? variantId,
        [FromQuery] long? outletId,
        [FromQuery] string? locationType,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo)
    {
        if (productId <= 0)
            return BadRequest(ApiResponse<StockTransactionReportDto>.ErrorResponse("productId is required"));

        try
        {
            // Server-side authorization & default-outlet resolution.
            // Frontend-supplied outletId is never trusted as-is.
            var (resolvedLocationId, resolvedLocationType) =
                await _outletAccess.ResolveAndAuthorizeLocationAsync(outletId, locationType);

            var report = await _inventoryReportService.GetStockTransactionReportAsync(
                productId, variantId, resolvedLocationId, resolvedLocationType, dateFrom, dateTo);

            return Ok(ApiResponse<StockTransactionReportDto>.SuccessResponse(report));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<StockTransactionReportDto>.ErrorResponse(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<StockTransactionReportDto>.ErrorResponse(ex.Message));
        }
    }
}
