using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailPOS.API.Authorization;
using RetailPOS.API.Documents.Pdf;
using RetailPOS.API.DTOs.Barcode;
using RetailPOS.API.DTOs.Product;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Infrastructure.Data;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/barcodes")]
[Authorize(Policy = "barcode.view")]
public class BarcodesController : ControllerBase
{
    private readonly IBarcodeService _barcodeService;
    private readonly IBarcodeLabelPdfService _barcodeLabelPdfService;
    private readonly ITenantAccessService _tenantAccessService;
    private readonly ISettingsService _settingsService;
    private readonly RetailPOSDbContext _dbContext;

    public BarcodesController(
        IBarcodeService barcodeService,
        IBarcodeLabelPdfService barcodeLabelPdfService,
        ITenantAccessService tenantAccessService,
        ISettingsService settingsService,
        RetailPOSDbContext dbContext)
    {
        _barcodeService = barcodeService;
        _barcodeLabelPdfService = barcodeLabelPdfService;
        _tenantAccessService = tenantAccessService;
        _settingsService = settingsService;
        _dbContext = dbContext;
    }

    [HttpGet("variants/search")]
    public async Task<ActionResult<ApiResponse<List<ProductVariantSearchDto>>>> SearchVariants(
        [FromQuery] string query = "",
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] long? locationId = null,
        [FromQuery] string? locationType = null)
    {
        var variants = await _barcodeService.SearchVariantsAsync(query, pageNumber, pageSize, locationId, locationType);
        return Ok(ApiResponse<List<ProductVariantSearchDto>>.SuccessResponse(variants));
    }

    [HttpPost("print-history/search")]
    public async Task<ActionResult<ApiResponse<BarcodePrintHistoryListDto>>> SearchPrintHistory([FromBody] BarcodePrintHistorySearchDto dto)
    {
        var result = await _barcodeService.SearchPrintHistoryAsync(dto);
        return Ok(ApiResponse<BarcodePrintHistoryListDto>.SuccessResponse(result));
    }

    [HttpGet("print-history/{id:long}")]
    public async Task<ActionResult<ApiResponse<BarcodePrintHistoryDto>>> GetPrintHistoryById(long id)
    {
        var row = await _barcodeService.GetPrintHistoryByIdAsync(id);
        return Ok(ApiResponse<BarcodePrintHistoryDto>.SuccessResponse(row));
    }

    [HttpPost("print-history/single")]
    [Authorize(Policy = "barcode.print")]
    public async Task<ActionResult<ApiResponse<BarcodePrintHistoryDto>>> RecordSinglePrint([FromBody] RecordBarcodePrintHistoryDto dto)
    {
        var row = await _barcodeService.RecordPrintHistoryAsync(dto, ResolveUserId());
        return Ok(ApiResponse<BarcodePrintHistoryDto>.SuccessResponse(row, "Barcode print history recorded"));
    }

    [HttpPost("print-history/bulk")]
    [Authorize(Policy = "barcode.bulk_print")]
    public async Task<ActionResult<ApiResponse<BarcodePrintHistoryDto>>> RecordBulkPrint([FromBody] RecordBarcodePrintHistoryDto dto)
    {
        var row = await _barcodeService.RecordPrintHistoryAsync(dto, ResolveUserId());
        return Ok(ApiResponse<BarcodePrintHistoryDto>.SuccessResponse(row, "Bulk barcode print history recorded"));
    }

    [HttpPost("pdf")]
    [Authorize(Policy = "barcode.print")]
    public async Task<IActionResult> GeneratePdf([FromBody] RecordBarcodePrintHistoryDto dto, CancellationToken cancellationToken)
    {
        if (dto.Items.Count > 1 && !User.HasPermission("barcode.bulk_print"))
        {
            return Forbid();
        }

        dto.CompanyName = await ResolveCompanyNameAsync(dto.CompanyName, dto.OutletId, cancellationToken);

        var result = await _barcodeLabelPdfService.GenerateAsync(dto, cancellationToken);
        return File(result.Content, "application/pdf", result.FileName);
    }

    private async Task<string> ResolveCompanyNameAsync(string? requestedCompanyName, long? outletId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedCompanyName))
        {
            return requestedCompanyName.Trim();
        }

        var businessId = _tenantAccessService.EffectiveBusinessId;
        if (businessId.HasValue)
        {
            var businessName = await _dbContext.Businesses
                .Where(b => b.Id == businessId.Value)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(businessName))
            {
                return businessName.Trim();
            }
        }

        var effectiveOutletId = outletId;
        if (!effectiveOutletId.HasValue)
        {
            var userId = ResolveUserId();
            if (userId.HasValue)
            {
                effectiveOutletId = await _dbContext.Users
                    .Where(u => u.Id == userId.Value)
                    .Select(u => u.OutletId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (effectiveOutletId.HasValue)
        {
            var outletName = await _dbContext.Outlets
                .Where(o => o.Id == effectiveOutletId.Value)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(outletName))
            {
                return outletName.Trim();
            }
        }

        var companySettings = await _settingsService.GetCompanySettingsAsync();
        if (!string.IsNullOrWhiteSpace(companySettings.CompanyName))
        {
            return companySettings.CompanyName.Trim();
        }

        return "IraniMart";
    }

    private long? ResolveUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
