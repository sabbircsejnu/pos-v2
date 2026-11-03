using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.DTOs.Barcode;
using RetailPOS.API.Models;
using RetailPOS.Application.Barcodes.Commands;
using RetailPOS.Application.Barcodes.Queries;
using RetailPOS.Application.Barcodes.ReadModels;
using RetailPOS.Application.Barcodes.WriteModels;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/barcode-templates")]
[Authorize(Policy = "barcode.view")]
public class BarcodeTemplatesController : ControllerBase
{
    private readonly ISender _sender;

    public BarcodeTemplatesController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<BarcodeTemplateDto>>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var rows = await _sender.Send(new GetBarcodeTemplatesQuery(includeInactive), cancellationToken);
        return Ok(ApiResponse<List<BarcodeTemplateDto>>.SuccessResponse(
            rows.Select(ToApi).ToList()));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<BarcodeTemplateDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var row = await _sender.Send(new GetBarcodeTemplateByIdQuery(id), cancellationToken);
        return Ok(ApiResponse<BarcodeTemplateDto>.SuccessResponse(ToApi(row)));
    }

    [HttpPost]
    [Authorize(Policy = "barcode.template_manage")]
    public async Task<ActionResult<ApiResponse<BarcodeTemplateDto>>> Create(
        [FromBody] CreateBarcodeTemplateDto dto,
        CancellationToken cancellationToken)
    {
        var created = await _sender.Send(
            new CreateBarcodeTemplateCommand(ToWriteModel(dto), ResolveUserId()),
            cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Id },
            ApiResponse<BarcodeTemplateDto>.SuccessResponse(ToApi(created), "Barcode template created successfully"));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "barcode.template_manage")]
    public async Task<ActionResult<ApiResponse<BarcodeTemplateDto>>> Update(
        long id,
        [FromBody] UpdateBarcodeTemplateDto dto,
        CancellationToken cancellationToken)
    {
        var updated = await _sender.Send(
            new UpdateBarcodeTemplateCommand(id, ToWriteModel(dto), ResolveUserId()),
            cancellationToken);
        return Ok(ApiResponse<BarcodeTemplateDto>.SuccessResponse(ToApi(updated), "Barcode template updated successfully"));
    }

    [HttpPost("{id:long}/set-default")]
    [Authorize(Policy = "barcode.template_manage")]
    public async Task<ActionResult<ApiResponse>> SetDefault(long id, CancellationToken cancellationToken)
    {
        await _sender.Send(new SetDefaultBarcodeTemplateCommand(id, ResolveUserId()), cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Barcode template set as default"));
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "barcode.template_manage")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteBarcodeTemplateCommand(id), cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Barcode template deleted successfully"));
    }

    private long? ResolveUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private static BarcodeTemplateWriteModel ToWriteModel(CreateBarcodeTemplateDto dto) =>
        ToWriteModel(
            dto.Name,
            dto.TemplateType,
            dto.PaperType,
            dto.LabelWidthMm,
            dto.LabelHeightMm,
            dto.IsDefault,
            dto.IsActive,
            dto.Fields);

    private static BarcodeTemplateWriteModel ToWriteModel(UpdateBarcodeTemplateDto dto) =>
        ToWriteModel(
            dto.Name,
            dto.TemplateType,
            dto.PaperType,
            dto.LabelWidthMm,
            dto.LabelHeightMm,
            dto.IsDefault,
            dto.IsActive,
            dto.Fields);

    private static BarcodeTemplateWriteModel ToWriteModel(
        string name,
        string templateType,
        string paperType,
        decimal labelWidthMm,
        decimal labelHeightMm,
        bool isDefault,
        bool isActive,
        IEnumerable<UpsertBarcodeTemplateFieldDto> fields) =>
        new(
            name,
            templateType,
            paperType,
            labelWidthMm,
            labelHeightMm,
            isDefault,
            isActive,
            fields.Select(field => new BarcodeTemplateFieldWriteModel(
                field.FieldKey,
                field.IsEnabled,
                field.SortOrder,
                field.X,
                field.Y,
                field.Width,
                field.Height,
                field.FontSize,
                field.FontWeight,
                field.Align)).ToArray());

    private static BarcodeTemplateDto ToApi(BarcodeTemplateReadModel template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        TemplateType = template.TemplateType,
        PaperType = template.PaperType,
        LabelWidthMm = template.LabelWidthMm,
        LabelHeightMm = template.LabelHeightMm,
        IsDefault = template.IsDefault,
        IsActive = template.IsActive,
        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt,
        Fields = template.Fields.Select(field => new BarcodeTemplateFieldDto
        {
            Id = field.Id,
            FieldKey = field.FieldKey,
            IsEnabled = field.IsEnabled,
            SortOrder = field.SortOrder,
            X = field.X,
            Y = field.Y,
            Width = field.Width,
            Height = field.Height,
            FontSize = field.FontSize,
            FontWeight = field.FontWeight,
            Align = field.Align
        }).ToList()
    };
}
