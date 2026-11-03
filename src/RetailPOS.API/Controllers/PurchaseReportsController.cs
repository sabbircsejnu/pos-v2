using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Models;
using RetailPOS.Application.PurchaseOrders.Queries;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/reports/purchases")]
[Authorize(Policy = "purchases.view")]
public class PurchaseReportsController : ControllerBase
{
    private readonly ISender _sender;

    public PurchaseReportsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Returns aggregated purchase order summary</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<PurchaseSummaryDto>>> GetSummary(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var end   = endDate?.ToUniversalTime()   ?? DateTime.UtcNow;
        var start = startDate?.ToUniversalTime() ?? end.AddDays(-30);

        var report = await _sender.Send(
            new GetPurchaseSummaryQuery(start, end),
            cancellationToken);

        var result = new PurchaseSummaryDto
        {
            TotalOrders = report.TotalOrders,
            TotalAmount = report.TotalAmount,
            PendingApprovals = report.PendingApprovals,
            ReceivedOrders = report.ReceivedOrders
        };

        return Ok(ApiResponse<PurchaseSummaryDto>.SuccessResponse(result));
    }

    /// <summary>Returns purchase totals grouped by supplier</summary>
    [HttpGet("by-supplier")]
    public async Task<ActionResult<ApiResponse<List<PurchaseBySupplierDto>>>> GetBySupplier(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var end   = endDate?.ToUniversalTime()   ?? DateTime.UtcNow;
        var start = startDate?.ToUniversalTime() ?? end.AddDays(-30);

        var grouped = await _sender.Send(
            new GetPurchasesBySupplierQuery(start, end),
            cancellationToken);
        var result = grouped
            .Select(group => new PurchaseBySupplierDto
            {
                SupplierId = group.SupplierId,
                SupplierName = group.SupplierName,
                OrderCount = group.OrderCount,
                TotalAmount = group.TotalAmount
            })
            .ToList();

        return Ok(ApiResponse<List<PurchaseBySupplierDto>>.SuccessResponse(result));
    }
}
