using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/reports/sales")]
[Authorize]
public class SalesReportsController : ControllerBase
{
    private readonly ISalesReportService _salesReportService;

    public SalesReportsController(ISalesReportService salesReportService)
    {
        _salesReportService = salesReportService;
    }

    /// <summary>Returns aggregated sales summary</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<SalesReportDto>>> GetSummary(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] long? outletId,
        [FromQuery] string? paymentMethod)
    {
        var filter = new SalesReportFilterDto
        {
            StartDate     = startDate,
            EndDate       = endDate,
            OutletId      = outletId,
            PaymentMethod = paymentMethod
        };
        var result = await _salesReportService.GetSummaryAsync(filter);
        return Ok(ApiResponse<SalesReportDto>.SuccessResponse(result));
    }

    /// <summary>Returns top-selling products</summary>
    [HttpGet("top-products")]
    public async Task<ActionResult<ApiResponse<List<TopProductDto>>>> GetTopProducts(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] long? outletId,
        [FromQuery] int limit = 10)
    {
        var filter = new SalesReportFilterDto
        {
            StartDate = startDate,
            EndDate   = endDate,
            OutletId  = outletId
        };
        var result = await _salesReportService.GetTopProductsAsync(filter, limit);
        return Ok(ApiResponse<List<TopProductDto>>.SuccessResponse(result));
    }

    /// <summary>Returns sales grouped by outlet</summary>
    [HttpGet("by-outlet")]
    public async Task<ActionResult<ApiResponse<List<SalesByOutletDto>>>> GetByOutlet(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var filter = new SalesReportFilterDto { StartDate = startDate, EndDate = endDate };
        var result = await _salesReportService.GetSalesByOutletAsync(filter);
        return Ok(ApiResponse<List<SalesByOutletDto>>.SuccessResponse(result));
    }

    /// <summary>Returns sales grouped by payment method</summary>
    [HttpGet("by-payment-method")]
    public async Task<ActionResult<ApiResponse<List<SalesByPaymentMethodDto>>>> GetByPaymentMethod(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] long? outletId)
    {
        var filter = new SalesReportFilterDto
        {
            StartDate = startDate,
            EndDate   = endDate,
            OutletId  = outletId
        };
        var result = await _salesReportService.GetSalesByPaymentMethodAsync(filter);
        return Ok(ApiResponse<List<SalesByPaymentMethodDto>>.SuccessResponse(result));
    }

    /// <summary>Returns daily sales trend for the period</summary>
    [HttpGet("daily-trend")]
    public async Task<ActionResult<ApiResponse<List<DailySalesTrendDto>>>> GetDailyTrend(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] long? outletId)
    {
        var filter = new SalesReportFilterDto
        {
            StartDate = startDate,
            EndDate   = endDate,
            OutletId  = outletId
        };
        var result = await _salesReportService.GetDailySalesTrendAsync(filter);
        return Ok(ApiResponse<List<DailySalesTrendDto>>.SuccessResponse(result));
    }
}
