using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize(Policy = "sales.view")]
public class SalesController : ControllerBase
{
    private readonly ISaleService _saleService;
    private readonly IUserOutletAccessService _outletAccess;
    private readonly IFeatureEntitlementService _featureEntitlement;
    private readonly IAuditService _audit;
    private readonly ILogger<SalesController> _logger;

    public SalesController(
        ISaleService saleService,
        IUserOutletAccessService outletAccess,
        IFeatureEntitlementService featureEntitlement,
        IAuditService audit,
        ILogger<SalesController> logger)
    {
        _saleService = saleService;
        _outletAccess = outletAccess;
        _featureEntitlement = featureEntitlement;
        _audit = audit;
        _logger = logger;
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
        var existing = await _saleService.GetByIdAsync(id);
        await EnsureSaleOutletAuthorizedAsync(existing);

        var sale = await _saleService.GetReceiptAsync(id, TryGetCurrentUserId());
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale));
    }

    /// <summary>Register and return receipt payload for sale reprint</summary>
    [HttpPost("{id}/reprint")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Reprint(long id)
    {
        var existing = await _saleService.GetByIdAsync(id);
        await EnsureSaleOutletAuthorizedAsync(existing);

        var sale = await _saleService.ReprintReceiptAsync(id, TryGetCurrentUserId());
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale, "Receipt reprint payload generated"));
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
    [Authorize(Policy = "sales.create")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Create([FromBody] CreateSaleDto dto)
    {
        var now = DateTime.UtcNow;
        var canBackdate = User.HasPermission("sales.backdate");

        if (dto.SalesDate.HasValue)
        {
            if (dto.SalesDate.Value > now)
            {
                return BadRequest(ApiResponse<SaleDto>.ErrorResponse("Sales Order Date cannot be in the future."));
            }

            if (!canBackdate && dto.SalesDate.Value.Date < now.Date)
            {
                return BadRequest(ApiResponse<SaleDto>.ErrorResponse("You are not allowed to create backdated POS sales."));
            }
        }

        if (!canBackdate)
        {
            dto.SalesDate = now;
        }
        else if (!dto.SalesDate.HasValue)
        {
            dto.SalesDate = now;
        }

        // Server-side outlet enforcement: never trust the client-supplied OutletId.
        // Non-BusinessOwner is pinned to their default outlet; BusinessOwner can use
        // any outlet in their authorized set.
        dto.OutletId = await _outletAccess.EnforceWriteOutletAsync(dto.OutletId);

        var sale = await _saleService.CreateAsync(dto);

        await TryRecordBackdatedSaleAuditAsync(sale, dto.SalesDate);

        return CreatedAtAction(
            nameof(GetById),
            new { id = sale.Id },
            ApiResponse<SaleDto>.SuccessResponse(sale, "Sale created successfully"));
    }

    private async Task TryRecordBackdatedSaleAuditAsync(SaleDto sale, DateTime? requestedSalesDate)
    {
        try
        {
            var createdAt = sale.CreatedAt;
            var salesDate = sale.SaleDate;

            if (salesDate.Date >= createdAt.Date)
            {
                return;
            }

            var userId = TryGetCurrentUserId();

            await _audit.RecordAsync(new AuditEventInput
            {
                ActionType = AuditActionType.Create,
                Module = AuditModule.Sales,
                Summary = $"Backdated POS sale created: {sale.SaleNumber}",
                PrimaryEntity = ("Sale", sale.Id.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["userId"] = userId,
                    ["salesDate"] = salesDate,
                    ["createdAt"] = createdAt,
                    ["outletId"] = sale.OutletId,
                    ["terminalId"] = sale.TerminalId,
                    ["saleNumber"] = sale.SaleNumber,
                    ["requestedSalesDate"] = requestedSalesDate
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record backdated sale audit for sale {SaleId}", sale.Id);
        }
    }

    /// <summary>Void a completed sale</summary>
    [HttpPost("{id}/void")]
    [Authorize(Policy = "sales.void")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Void(long id, [FromBody] VoidSaleDto dto)
    {
        var existing = await _saleService.GetByIdAsync(id);
        await EnsureSaleOutletAuthorizedAsync(existing);

        var sale = await _saleService.VoidAsync(id, dto, TryGetCurrentUserId());
        return Ok(ApiResponse<SaleDto>.SuccessResponse(sale, "Sale voided successfully"));
    }

    /// <summary>Refund a sale</summary>
    [HttpPost("{id}/refund")]
    [Authorize(Policy = "sales.refund")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> Refund(long id, [FromBody] RefundSaleDto dto)
    {
        await _featureEntitlement.EnsureFeatureEnabledAsync("sales.refund");

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

    private long? TryGetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return long.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
