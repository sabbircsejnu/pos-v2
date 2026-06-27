using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.StockCount;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/stock-counts")]
[Authorize]
public class StockCountsController : ControllerBase
{
    public sealed class UploadStockCountRequest
    {
        public IFormFile File { get; set; } = default!;
    }

    public sealed class RejectStockCountRequest
    {
        public string? Reason { get; set; }
    }

    private readonly IStockCountService _service;

    public StockCountsController(IStockCountService service)
    {
        _service = service;
    }

    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<StockCountListDto>>> Search([FromBody] StockCountSearchDto dto)
    {
        if (!CanView())
        {
            return ForbidResponse<StockCountListDto>();
        }

        var result = await _service.SearchAsync(dto);
        return Ok(ApiResponse<StockCountListDto>.SuccessResponse(result));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> GetById(long id)
    {
        if (!CanView())
        {
            return ForbidResponse<StockCountDto>();
        }

        var dto = await _service.GetByIdAsync(id);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(dto));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Create([FromBody] CreateStockCountDto dto)
    {
        if (!User.HasPermission("StockCount.Create"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var created = await _service.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ApiResponse<StockCountDto>.SuccessResponse(created));
    }

    [HttpGet("{id:long}/download")]
    public async Task<IActionResult> Download(long id)
    {
        if (!User.HasPermission("StockCount.Download"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.ErrorResponse("Permission denied"));
        }

        var file = await _service.DownloadExcelAsync(id);
        return File(file.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.FileName);
    }

    [HttpGet("{id:long}/print")]
    public async Task<ActionResult<ApiResponse<StockCountPrintDto>>> Print(long id)
    {
        if (!User.HasPermission("StockCount.Print"))
        {
            return ForbidResponse<StockCountPrintDto>();
        }

        var dto = await _service.GetPrintDataAsync(id);
        return Ok(ApiResponse<StockCountPrintDto>.SuccessResponse(dto));
    }

    [HttpPost("{id:long}/upload")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Upload(long id, [FromForm] UploadStockCountRequest request)
    {
        if (!User.HasPermission("StockCount.Upload"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest(ApiResponse<StockCountDto>.ErrorResponse("An Excel file is required for upload."));
        }

        var updated = await _service.UploadAsync(id, userId, request.File);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(updated));
    }

    [HttpPost("{id:long}/submit")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Submit(long id)
    {
        if (!User.HasPermission("StockCount.Submit"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var updated = await _service.SubmitAsync(id, userId);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(updated));
    }

    [HttpPost("{id:long}/approve")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Approve(long id)
    {
        if (!User.HasPermission("StockCount.Approve"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var updated = await _service.ApproveAsync(id, userId);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(updated));
    }

    [HttpPost("{id:long}/reject")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Reject(long id, [FromBody] RejectStockCountRequest? request)
    {
        if (!User.HasPermission("StockCount.Approve"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var updated = await _service.RejectAsync(id, userId, request?.Reason);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(updated));
    }

    [HttpPost("{id:long}/reopen")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> Reopen(long id)
    {
        if (!User.HasPermission("StockCount.Reopen"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var updated = await _service.ReopenAsync(id, userId);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(updated));
    }

    [HttpPost("{id:long}/generate-adjustment-draft")]
    public async Task<ActionResult<ApiResponse<StockCountDto>>> GenerateAdjustmentDraft(long id)
    {
        if (!User.HasPermission("StockCount.Approve"))
        {
            return ForbidResponse<StockCountDto>();
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<StockCountDto>.ErrorResponse("User identity not found"));
        }

        var updated = await _service.GenerateAdjustmentDraftAsync(id, userId);
        return Ok(ApiResponse<StockCountDto>.SuccessResponse(updated));
    }

    private bool CanView()
    {
        return User.HasPermission("StockCount.ViewOwn") || User.HasPermission("StockCount.ViewAll");
    }

    private ActionResult<ApiResponse<T>> ForbidResponse<T>()
    {
        return StatusCode(StatusCodes.Status403Forbidden,
            ApiResponse<T>.ErrorResponse("You do not have permission to perform this action"));
    }

    private bool TryGetCurrentUserId(out long userId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return long.TryParse(userIdClaim, out userId);
    }
}
