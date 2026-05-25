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
    private readonly IUserOutletAccessService _outletAccess;

    public SalesController(ISaleService saleService, IUserOutletAccessService outletAccess)
    {
        _saleService = saleService;
        _outletAccess = outletAccess;
    }

    /// <summary>List sales with filters (query string)</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<SaleListDto>>> GetAll([FromQuery] SaleSearchDto searchDto)
    {
        searchDto.OutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(searchDto.OutletId);
        var result = await _saleService.SearchAsync(searchDto);
        return Ok(ApiResponse<SaleListDto>.SuccessResponse(result));
    }

    /// <summary>Get a sale by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> GetById(long id)
    {
        var sale = await _saleService.GetByIdAsync(id);
        await EnsureSaleOutletAuthorizedAsync(sale);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale));
    }

    /// <summary>Get receipt data for a sale</summary>
    [HttpGet("{id}/receipt")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> GetReceipt(long id)
    {
        var sale = await _saleService.GetReceiptAsync(id);
        await EnsureSaleOutletAuthorizedAsync(sale);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale));
    }

    /// <summary>Get today's sales summary</summary>
    [HttpGet("today/summary")]
    public async Task<ActionResult<ApiResponse<SaleSummaryDto>>> TodaysSummary([FromQuery] long? outletId = null)
    {
        outletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
        var summary = await _saleService.GetTodaysSummaryAsync(outletId);
        return Ok(ApiResponse<SaleSummaryDto>.SuccessResponse(summary));
    }

    /// <summary>Search sales with pagination (request body)</summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<SaleListDto>>> Search([FromBody] SaleSearchDto searchDto)
    {
        searchDto.OutletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(searchDto.OutletId);
        var result = await _saleService.SearchAsync(searchDto);
        return Ok(ApiResponse<SaleListDto>.SuccessResponse(result));
    }

    /// <summary>Create a new sale</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Create([FromBody] CreateSaleDto dto)
    {
        // Server-side outlet enforcement: never trust the client-supplied OutletId.
        // Non-BusinessOwner is pinned to their default outlet; BusinessOwner can use
        // any outlet in their authorized set.
        dto.OutletId = await _outletAccess.EnforceWriteOutletAsync(dto.OutletId);

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
        var existing = await _saleService.GetByIdAsync(id);
        await EnsureSaleOutletAuthorizedAsync(existing);

        var sale = await _saleService.VoidAsync(id, dto);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale, "Sale voided successfully"));
    }

    /// <summary>Refund a sale</summary>
    [HttpPost("{id}/refund")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Refund(long id, [FromBody] RefundSaleDto dto)
    {
        var existing = await _saleService.GetByIdAsync(id);
        await EnsureSaleOutletAuthorizedAsync(existing);

        var sale = await _saleService.RefundAsync(id, dto);
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale, "Sale refunded successfully"));
    }

    private async Task EnsureSaleOutletAuthorizedAsync(SaleDto sale)
    {
        // Throws UnauthorizedAccessException for non-BusinessOwner trying to touch
        // a sale that belongs to an outlet outside their authorized set.
        await _outletAccess.ResolveAndAuthorizeLocationAsync(sale.OutletId, "outlet");
    }
}
