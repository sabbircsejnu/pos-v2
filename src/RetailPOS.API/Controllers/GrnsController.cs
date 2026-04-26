using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Grn;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using System.Security.Claims;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/grns")]
[Authorize]
public class GrnsController : ControllerBase
{
    private readonly IGrnService _grnService;

    public GrnsController(IGrnService grnService)
    {
        _grnService = grnService;
    }

    /// <summary>
    /// Get all GRNs with optional filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<GrnDto>>>> GetAll(
        [FromQuery] long? poId = null,
        [FromQuery] string? status = null)
    {
        var grns = await _grnService.GetAllAsync(poId, status);
        return Ok(ApiResponse<List<GrnDto>>.SuccessResponse(grns));
    }

    /// <summary>
    /// Search GRNs with pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<GrnListDto>>> Search([FromBody] GrnSearchDto searchDto)
    {
        var result = await _grnService.SearchAsync(searchDto);
        return Ok(ApiResponse<GrnListDto>.SuccessResponse(result));
    }

    /// <summary>
    /// Get GRN by ID with full details
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<GrnDto>>> GetById(long id)
    {
        var grn = await _grnService.GetByIdAsync(id);
        return Ok(ApiResponse<GrnDto>.SuccessResponse(grn));
    }

    /// <summary>
    /// Get purchase orders awaiting receipt (status = "approved")
    /// </summary>
    [HttpGet("pending-pos")]
    public async Task<ActionResult<ApiResponse<List<PurchaseOrderForGrnDto>>>> GetPendingPos()
    {
        var pos = await _grnService.GetPendingReceiptPOsAsync();
        return Ok(ApiResponse<List<PurchaseOrderForGrnDto>>.SuccessResponse(pos));
    }

    /// <summary>
    /// Get variance report (ordered vs received quantities) for a single GRN
    /// </summary>
    [HttpGet("{id}/variance")]
    public async Task<ActionResult<ApiResponse<GrnVarianceDto>>> GetVariance(long id)
    {
        var variance = await _grnService.GetVarianceAsync(id);
        return Ok(ApiResponse<GrnVarianceDto>.SuccessResponse(variance));
    }

    /// <summary>
    /// Get cumulative variance for an entire PO across all its GRNs
    /// (shows remaining quantities outstanding per line item)
    /// </summary>
    [HttpGet("po/{poId}/variance")]                  // NEW
    public async Task<ActionResult<ApiResponse<GrnPoVarianceDto>>> GetPoVariance(long poId)
    {
        var variance = await _grnService.GetPoVarianceAsync(poId);
        return Ok(ApiResponse<GrnPoVarianceDto>.SuccessResponse(variance));
    }

    /// <summary>
    /// Create a new GRN from an approved PO
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<GrnDto>>> Create([FromBody] CreateGrnDto dto)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        long? userId = long.TryParse(userIdClaim, out var parsedId) ? parsedId : null;

        var grn = await _grnService.CreateAsync(dto, userId);
        return CreatedAtAction(
            nameof(GetById),
            new { id = grn.Id },
            ApiResponse<GrnDto>.SuccessResponse(grn, "GRN created successfully"));
    }

    /// <summary>
    /// Complete a GRN: update inventory stock and PO status
    /// </summary>
    [HttpPost("{id}/complete")]
    public async Task<ActionResult<ApiResponse<GrnDto>>> Complete(long id)
    {
        var grn = await _grnService.CompleteAsync(id);
        return Ok(ApiResponse<GrnDto>.SuccessResponse(grn, "GRN completed and stock updated successfully"));
    }
}
