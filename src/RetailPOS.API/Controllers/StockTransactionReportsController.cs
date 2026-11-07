using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using MediatR;
using RetailPOS.Application.Inventory.Queries;
using RetailPOS.Application.Inventory.ReadModels;

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
    private readonly IUserOutletAccessService _outletAccess;
    private readonly ISender _sender;

    public StockTransactionReportsController(
        IUserOutletAccessService outletAccess,
        ISender sender)
    {
        _outletAccess = outletAccess;
        _sender = sender;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<StockTransactionReportDto>>> Get(
        [FromQuery] long productId,
        [FromQuery] long? variantId,
        [FromQuery] long? outletId,
        [FromQuery] string? locationType,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        CancellationToken cancellationToken)
    {
        if (productId <= 0)
            return BadRequest(ApiResponse<StockTransactionReportDto>.ErrorResponse("productId is required"));

        try
        {
            // Server-side authorization & default-outlet resolution.
            // Frontend-supplied outletId is never trusted as-is.
            var (resolvedLocationId, resolvedLocationType) =
                await _outletAccess.ResolveAndAuthorizeLocationAsync(outletId, locationType);

            var report = await _sender.Send(
                new GetStockTransactionReportQuery(new StockTransactionReportFilter(
                    productId,
                    variantId,
                    resolvedLocationId,
                    resolvedLocationType,
                    dateFrom,
                    dateTo)),
                cancellationToken);
            var includeCost = User.CanViewCost();

            return Ok(ApiResponse<StockTransactionReportDto>.SuccessResponse(new StockTransactionReportDto
            {
                ProductId = report.ProductId,
                ProductName = report.ProductName,
                ProductCode = report.ProductCode,
                Level = report.Level,
                VariantId = report.VariantId,
                VariantCode = report.VariantCode,
                VariantName = report.VariantName,
                VariantAttributes = report.VariantAttributes,
                Sku = report.Sku,
                OutletId = report.LocationId,
                LocationType = report.LocationType,
                DateFrom = report.DateFrom,
                DateTo = report.DateTo,
                Summary = new StockTransactionReportSummaryDto
                {
                    OpeningStock = report.Summary.OpeningStock,
                    StockIn = report.Summary.StockIn,
                    StockOut = report.Summary.StockOut,
                    ClosingStock = report.Summary.ClosingStock,
                    CurrentStock = report.Summary.CurrentStock
                },
                Rows = report.Rows.Select(row => new StockTransactionReportRowDto
                {
                    Id = row.Id,
                    TransactionDate = row.TransactionDate,
                    LocationId = row.LocationId,
                    LocationType = row.LocationType,
                    LocationName = row.LocationName,
                    TransactionType = row.TransactionType,
                    ReferenceType = row.ReferenceType,
                    ReferenceId = row.ReferenceId,
                    ReferenceNo = row.ReferenceNumber,
                    VariantId = row.VariantId,
                    VariantCode = row.VariantCode,
                    VariantAttributes = row.VariantAttributes,
                    QuantityIn = row.QuantityIn,
                    QuantityOut = row.QuantityOut,
                    RunningBalance = row.RunningBalance,
                    // Same rule as the stock ledger: a remark quoting a cost needs products.view_cost.
                    Remarks = includeCost || row.Remarks?.Contains("cost", StringComparison.OrdinalIgnoreCase) != true
                        ? row.Remarks
                        : "Cost details require the view-cost permission.",
                    CreatedByName = row.CreatedByName
                }).ToList()
            }));
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
