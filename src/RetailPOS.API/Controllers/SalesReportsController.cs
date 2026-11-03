using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using MediatR;
using RetailPOS.Application.SalesReports;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/reports/sales")]
[Authorize(Policy = "reports.sales")]
public class SalesReportsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IUserOutletAccessService _outletAccess;

    public SalesReportsController(
        ISender sender,
        IUserOutletAccessService outletAccess)
    {
        _sender = sender;
        _outletAccess = outletAccess;
    }

    /// <summary>Returns aggregated sales summary</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<SalesReportDto>>> GetSummary(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] long? outletId,
        [FromQuery] string? paymentMethod,
        CancellationToken cancellationToken)
    {
        outletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
        var result = await _sender.Send(
            new GetSalesReportSummaryQuery(startDate, endDate, outletId, paymentMethod),
            cancellationToken);
        return Ok(ApiResponse<SalesReportDto>.SuccessResponse(new SalesReportDto
        {
            TotalRevenue = result.TotalRevenue,
            TotalDiscount = result.TotalDiscount,
            TotalTax = result.TotalTax,
            NetRevenue = result.NetRevenue,
            TotalTransactions = result.TotalTransactions,
            AverageOrderValue = result.AverageOrderValue
        }));
    }

    /// <summary>Returns top-selling products</summary>
    [HttpGet("top-products")]
    public async Task<ActionResult<ApiResponse<List<TopProductDto>>>> GetTopProducts(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] long? outletId,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        outletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
        var result = await _sender.Send(
            new GetTopSalesProductsQuery(startDate, endDate, outletId, limit),
            cancellationToken);
        return Ok(ApiResponse<List<TopProductDto>>.SuccessResponse(result.Select(item => new TopProductDto
        {
            VariantId = item.VariantId,
            ProductName = item.ProductName,
            Sku = item.Sku,
            TotalQuantity = item.TotalQuantity,
            TotalRevenue = item.TotalRevenue
        }).ToList()));
    }

    /// <summary>Returns sales grouped by outlet</summary>
    [HttpGet("by-outlet")]
    public async Task<ActionResult<ApiResponse<List<SalesByOutletDto>>>> GetByOutlet(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        // Cross-outlet aggregation. Non-BusinessOwner only sees their own outlet's row.
        var scopedOutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(null);
        var result = await _sender.Send(new GetSalesByOutletQuery(startDate, endDate), cancellationToken);
        if (scopedOutletId.HasValue)
            result = result.Where(r => r.OutletId == scopedOutletId.Value).ToArray();
        return Ok(ApiResponse<List<SalesByOutletDto>>.SuccessResponse(result.Select(item => new SalesByOutletDto
        {
            OutletId = item.OutletId,
            OutletName = item.OutletName,
            TransactionCount = item.TransactionCount,
            TotalRevenue = item.TotalRevenue
        }).ToList()));
    }

    /// <summary>Returns sales grouped by payment method</summary>
    [HttpGet("by-payment-method")]
    public async Task<ActionResult<ApiResponse<List<SalesByPaymentMethodDto>>>> GetByPaymentMethod(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] long? outletId,
        CancellationToken cancellationToken)
    {
        outletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
        var result = await _sender.Send(
            new GetSalesByPaymentMethodQuery(startDate, endDate, outletId),
            cancellationToken);
        return Ok(ApiResponse<List<SalesByPaymentMethodDto>>.SuccessResponse(result.Select(item => new SalesByPaymentMethodDto
        {
            PaymentMethod = item.PaymentMethod,
            Count = item.Count,
            Amount = item.Amount
        }).ToList()));
    }

    /// <summary>Returns daily sales trend for the period</summary>
    [HttpGet("daily-trend")]
    public async Task<ActionResult<ApiResponse<List<DailySalesTrendDto>>>> GetDailyTrend(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] long? outletId,
        CancellationToken cancellationToken)
    {
        outletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
        var result = await _sender.Send(
            new GetDailySalesTrendQuery(startDate, endDate, outletId),
            cancellationToken);
        return Ok(ApiResponse<List<DailySalesTrendDto>>.SuccessResponse(result.Select(item => new DailySalesTrendDto
        {
            Date = item.Date,
            TransactionCount = item.TransactionCount,
            Revenue = item.Revenue
        }).ToList()));
    }
}
