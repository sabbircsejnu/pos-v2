using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Barcode;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/barcode-templates")]
[Authorize(Policy = "barcode.view")]
public class BarcodeTemplatesController : ControllerBase
{
    private readonly IBarcodeService _barcodeService;

    public BarcodeTemplatesController(IBarcodeService barcodeService)
    {
        _barcodeService = barcodeService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<BarcodeTemplateDto>>>> GetAll([FromQuery] bool includeInactive = false)
    {
        var rows = await _barcodeService.GetTemplatesAsync(includeInactive);
        return Ok(ApiResponse<List<BarcodeTemplateDto>>.SuccessResponse(rows));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<BarcodeTemplateDto>>> GetById(long id)
    {
        var row = await _barcodeService.GetTemplateByIdAsync(id);
        return Ok(ApiResponse<BarcodeTemplateDto>.SuccessResponse(row));
    }

    [HttpPost]
    [Authorize(Policy = "barcode.template_manage")]
    public async Task<ActionResult<ApiResponse<BarcodeTemplateDto>>> Create([FromBody] CreateBarcodeTemplateDto dto)
    {
        var created = await _barcodeService.CreateTemplateAsync(dto, ResolveUserId());
        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Id },
            ApiResponse<BarcodeTemplateDto>.SuccessResponse(created, "Barcode template created successfully"));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "barcode.template_manage")]
    public async Task<ActionResult<ApiResponse<BarcodeTemplateDto>>> Update(long id, [FromBody] UpdateBarcodeTemplateDto dto)
    {
        var updated = await _barcodeService.UpdateTemplateAsync(id, dto, ResolveUserId());
        return Ok(ApiResponse<BarcodeTemplateDto>.SuccessResponse(updated, "Barcode template updated successfully"));
    }

    [HttpPost("{id:long}/set-default")]
    [Authorize(Policy = "barcode.template_manage")]
    public async Task<ActionResult<ApiResponse>> SetDefault(long id)
    {
        await _barcodeService.SetDefaultTemplateAsync(id, ResolveUserId());
        return Ok(ApiResponse.SuccessResponse("Barcode template set as default"));
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "barcode.template_manage")]
    public async Task<ActionResult<ApiResponse>> Delete(long id)
    {
        await _barcodeService.DeleteTemplateAsync(id);
        return Ok(ApiResponse.SuccessResponse("Barcode template deleted successfully"));
    }

    private long? ResolveUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
