using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.Authorization;
using RetailPOS.API.Documents.Pdf;
using RetailPOS.API.DTOs.Barcode;
using RetailPOS.API.DTOs.Product;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Barcodes.Commands;
using RetailPOS.Application.Barcodes.Queries;
using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Application.Products.Queries;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Application.Settings.Queries;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/barcodes")]
[Authorize(Policy = "barcode.view")]
public class BarcodesController : ControllerBase
{
    private readonly IBarcodeLabelPdfService _barcodeLabelPdfService;
    private readonly IVariantDisplayFormatterService _variantDisplayFormatter;
    private readonly ISender _sender;
    private readonly IUserOutletAccessService _outletAccess;

    public BarcodesController(
        IBarcodeLabelPdfService barcodeLabelPdfService,
        IVariantDisplayFormatterService variantDisplayFormatter,
        ISender sender,
        IUserOutletAccessService outletAccess)
    {
        _barcodeLabelPdfService = barcodeLabelPdfService;
        _variantDisplayFormatter = variantDisplayFormatter;
        _sender = sender;
        _outletAccess = outletAccess;
    }

    [HttpGet("variants/search")]
    public async Task<ActionResult<ApiResponse<List<ProductVariantSearchDto>>>> SearchVariants(
        [FromQuery] string query = "",
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] long? locationId = null,
        [FromQuery] string? locationType = null,
        CancellationToken cancellationToken = default)
    {
        (long? LocationId, string? LocationType) authorizedLocation;
        try
        {
            authorizedLocation = await StockLocationRequestAuthorizer.ResolveAndAuthorizeAsync(
                _outletAccess,
                locationId,
                locationType,
                Request.Query.ContainsKey("locationType"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<List<ProductVariantSearchDto>>.ErrorResponse(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<List<ProductVariantSearchDto>>.ErrorResponse(ex.Message));
        }

        var results = await _sender.Send(
            new SearchProductVariantsQuery(new ProductVariantSearchCriteria(
                query,
                Math.Max(1, pageNumber),
                Math.Clamp(pageSize, 1, 100),
                authorizedLocation.LocationId,
                authorizedLocation.LocationType)),
            cancellationToken);
        var variants = new List<ProductVariantSearchDto>(results.Count);
        foreach (var result in results)
        {
            variants.Add(new ProductVariantSearchDto
            {
                Id = result.Id,
                ProductId = result.ProductId,
                ProductName = result.ProductName,
                ProductCode = result.ProductCode,
                Name = result.Name,
                Sku = result.Sku,
                Barcode = result.Barcode,
                Attributes = result.Attributes,
                VariantDisplay = await _variantDisplayFormatter.FormatAsync(
                    result.Attributes,
                    cancellationToken),
                FinalPrice = result.FinalPrice,
                CostPrice = result.CostPrice,
                StockQuantity = result.StockQuantity,
                PrimaryImageThumb = result.PrimaryImageThumb
            });
        }

        return Ok(ApiResponse<List<ProductVariantSearchDto>>.SuccessResponse(variants));
    }

    [HttpPost("print-history/search")]
    public async Task<ActionResult<ApiResponse<BarcodePrintHistoryListDto>>> SearchPrintHistory(
        [FromBody] BarcodePrintHistorySearchDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SearchBarcodePrintHistoryQuery(new BarcodePrintHistorySearchCriteria(
                dto.TemplateId,
                dto.PrintedByUserId,
                dto.StartDate,
                dto.EndDate,
                dto.PageNumber,
                dto.PageSize)),
            cancellationToken);
        return Ok(ApiResponse<BarcodePrintHistoryListDto>.SuccessResponse(new BarcodePrintHistoryListDto
        {
            Rows = result.Rows.Select(ToApi).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        }));
    }

    [HttpGet("print-history/{id:long}")]
    public async Task<ActionResult<ApiResponse<BarcodePrintHistoryDto>>> GetPrintHistoryById(
        long id,
        CancellationToken cancellationToken)
    {
        var row = await _sender.Send(new GetBarcodePrintHistoryByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<BarcodePrintHistoryDto>.SuccessResponse(ToApi(row)));
    }

    [HttpPost("print-history/single")]
    [Authorize(Policy = "barcode.print")]
    public async Task<ActionResult<ApiResponse<BarcodePrintHistoryDto>>> RecordSinglePrint(
        [FromBody] RecordBarcodePrintHistoryDto dto,
        CancellationToken cancellationToken)
    {
        var row = await _sender.Send(ToCommand(dto), cancellationToken);
        return Ok(ApiResponse<BarcodePrintHistoryDto>.SuccessResponse(ToApi(row), "Barcode print history recorded"));
    }

    [HttpPost("print-history/bulk")]
    [Authorize(Policy = "barcode.bulk_print")]
    public async Task<ActionResult<ApiResponse<BarcodePrintHistoryDto>>> RecordBulkPrint(
        [FromBody] RecordBarcodePrintHistoryDto dto,
        CancellationToken cancellationToken)
    {
        var row = await _sender.Send(ToCommand(dto), cancellationToken);
        return Ok(ApiResponse<BarcodePrintHistoryDto>.SuccessResponse(ToApi(row), "Bulk barcode print history recorded"));
    }

    [HttpPost("pdf")]
    [Authorize(Policy = "barcode.print")]
    public async Task<IActionResult> GeneratePdf([FromBody] RecordBarcodePrintHistoryDto dto, CancellationToken cancellationToken)
    {
        if (dto.Items.Count > 1 && !User.HasPermission("barcode.bulk_print"))
        {
            return Forbid();
        }

        dto.CompanyName = await ResolveCompanyNameAsync(
            dto.CompanyName,
            dto.OutletId,
            ResolveUserId(),
            cancellationToken);

        var result = await _barcodeLabelPdfService.GenerateAsync(dto, cancellationToken);
        return File(result.Content, "application/pdf", result.FileName);
    }

    private async Task<string> ResolveCompanyNameAsync(
        string? requestedCompanyName,
        long? outletId,
        long? userId,
        CancellationToken cancellationToken)
    {
        var companyName = await _sender.Send(
            new ResolveBarcodeCompanyNameQuery(requestedCompanyName, outletId, userId),
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(companyName))
            return companyName;

        var companySettings = (await _sender.Send(new GetSystemSettingsQuery(), cancellationToken)).Company;
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

    private RecordBarcodePrintHistoryCommand ToCommand(RecordBarcodePrintHistoryDto dto) =>
        new(
            dto.TemplateId,
            dto.OutletId,
            ResolveUserId(),
            dto.PrintMode,
            dto.LabelWidthMm,
            dto.LabelHeightMm,
            dto.SourceModule,
            dto.SourceReferenceType,
            dto.SourceReferenceId,
            dto.Items.Select(item => new RecordBarcodePrintHistoryItem(
                item.VariantId,
                item.ProductName,
                item.VariantName,
                item.VariantSku,
                item.BarcodeValue,
                item.VariantAttributes,
                item.SellingPrice,
                item.QuantityPrinted)).ToArray());

    private static BarcodePrintHistoryDto ToApi(BarcodePrintHistoryReadModel history) => new()
    {
        Id = history.Id,
        TemplateId = history.TemplateId,
        TemplateName = history.TemplateName,
        OutletId = history.OutletId,
        OutletName = history.OutletName,
        PrintedByUserId = history.PrintedByUserId,
        PrintedByUserName = history.PrintedByUserName,
        PrintMode = history.PrintMode,
        LabelWidthMm = history.LabelWidthMm,
        LabelHeightMm = history.LabelHeightMm,
        TotalLabels = history.TotalLabels,
        SourceModule = history.SourceModule,
        SourceReferenceType = history.SourceReferenceType,
        SourceReferenceId = history.SourceReferenceId,
        PrintedAt = history.PrintedAt,
        Items = history.Items.Select(item => new BarcodePrintHistoryItemDto
        {
            Id = item.Id,
            VariantId = item.VariantId,
            ProductName = item.ProductName,
            VariantName = item.VariantName,
            VariantSku = item.VariantSku,
            BarcodeValue = item.BarcodeValue,
            VariantAttributes = item.VariantAttributes,
            SellingPrice = item.SellingPrice,
            QuantityPrinted = item.QuantityPrinted
        }).ToList()
    };
}
