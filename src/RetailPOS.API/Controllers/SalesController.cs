using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize]
public class SalesController : ControllerBase
{
    private readonly ISaleService _saleService;

    public SalesController(ISaleService saleService)
    {
        _saleService = saleService;
    }

    /// <summary>List sales with filters (query string)</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<SaleListDto>>> GetAll([FromQuery] SaleSearchDto searchDto)
    {
        var result = await _saleService.SearchAsync(searchDto);
        return Ok(ApiResponse<SaleListDto>.SuccessResponse(result));
    }

    /// <summary>Get a sale by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> GetById(long id)
    {
        var sale = await _saleService.GetByIdAsync(id);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale));
    }

    /// <summary>Get receipt data for a sale</summary>
    [HttpGet("{id}/receipt")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> GetReceipt(long id)
    {
        var sale = await _saleService.GetReceiptAsync(id);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale));
    }

    /// <summary>Get today's sales summary</summary>
    [HttpGet("today/summary")]
    public async Task<ActionResult<ApiResponse<SaleSummaryDto>>> TodaysSummary([FromQuery] long? outletId = null)
    {
        var summary = await _saleService.GetTodaysSummaryAsync(outletId);
        return Ok(ApiResponse<SaleSummaryDto>.SuccessResponse(summary));
    }

    /// <summary>Search sales with pagination (request body)</summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<SaleListDto>>> Search([FromBody] SaleSearchDto searchDto)
    {
        var result = await _saleService.SearchAsync(searchDto);
        return Ok(ApiResponse<SaleListDto>.SuccessResponse(result));
    }

    /// <summary>Create a new sale</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Create([FromBody] CreateSaleDto dto)
    {
        var sale = await _saleService.CreateAsync(dto);
        return CreatedAtAction(
            nameof(GetById),
            new { id = sale.Id },
            ApiResponse<SaleDto>.SuccessResponse(sale, "Sale created successfully"));
    }

    /// <summary>Void a sale (same day only)</summary>
    [HttpPost("{id}/void")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Void(long id, [FromBody] VoidSaleDto dto)
    {
        var sale = await _saleService.VoidAsync(id, dto);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale, "Sale voided successfully"));
    }

    /// <summary>Refund a sale</summary>
    [HttpPost("{id}/refund")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Refund(long id, [FromBody] RefundSaleDto dto)
    {
        var sale = await _saleService.RefundAsync(id, dto);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale, "Sale refunded successfully"));
    }
}
