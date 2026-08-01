using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/journals")]
[Authorize(Policy = "accounting.journals.view")]
public class AccountingJournalsController : ControllerBase
{
    private readonly IJournalEntryService _journalEntryService;
    private readonly IUserOutletAccessService _outletAccess;

    public AccountingJournalsController(IJournalEntryService journalEntryService, IUserOutletAccessService outletAccess)
    {
        _journalEntryService = journalEntryService;
        _outletAccess = outletAccess;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<JournalListDto>>> Search(
        [FromQuery] DateTime? postingDateStart,
        [FromQuery] DateTime? postingDateEnd,
        [FromQuery] DateTime? businessDateStart,
        [FromQuery] DateTime? businessDateEnd,
        [FromQuery] string? status,
        [FromQuery] long? outletId,
        [FromQuery] string? journalType,
        [FromQuery] string? journalNumber,
        [FromQuery] long? accountId,
        [FromQuery] string? referenceType,
        [FromQuery] string? referenceNumber,
        [FromQuery] long? createdBy,
        [FromQuery] long? postedBy,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        outletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);

        var result = await _journalEntryService.SearchAsync(new JournalSearchDto
        {
            PostingDateStart = postingDateStart,
            PostingDateEnd = postingDateEnd,
            BusinessDateStart = businessDateStart,
            BusinessDateEnd = businessDateEnd,
            Status = status,
            OutletId = outletId,
            JournalType = journalType,
            JournalNumber = journalNumber,
            AccountId = accountId,
            ReferenceType = referenceType,
            ReferenceNumber = referenceNumber,
            CreatedBy = createdBy,
            PostedBy = postedBy,
            SortBy = sortBy,
            SortDirection = sortDirection,
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        return Ok(ApiResponse<JournalListDto>.SuccessResponse(result));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> GetById(long id)
    {
        var result = await _journalEntryService.GetByIdAsync(id);
        await EnsureJournalLocationAuthorizedAsync(result);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result));
    }

    [HttpPost]
    [Authorize(Policy = "accounting.journals.create")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> Create([FromBody] CreateJournalEntryDto dto)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        dto.OutletId = await _outletAccess.EnforceWriteOutletAsync(dto.OutletId);

        var result = await _journalEntryService.CreateManualAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal draft created successfully"));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "accounting.journals.update")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> UpdateDraft(long id, [FromBody] UpdateJournalEntryDto dto)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id);
        dto.OutletId = await _outletAccess.EnforceWriteOutletAsync(dto.OutletId);

        var result = await _journalEntryService.UpdateDraftAsync(id, dto, userId);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal draft updated successfully"));
    }

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = "accounting.journals.cancel")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> CancelDraft(long id, [FromBody] CancelJournalEntryDto? dto)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id);

        var result = await _journalEntryService.CancelDraftAsync(id, dto?.Reason, userId);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal draft cancelled successfully"));
    }

    [HttpPost("{id:long}/post")]
    [Authorize(Policy = "accounting.journals.post")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> Post(long id)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id);

        var result = await _journalEntryService.PostAsync(id, userId);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal posted successfully"));
    }

    [HttpPost("{id:long}/reverse")]
    [Authorize(Policy = "accounting.journals.reverse")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> Reverse(long id, [FromBody] ReverseJournalEntryDto dto)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id);

        var result = await _journalEntryService.ReverseAsync(id, dto, userId);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal reversed successfully"));
    }

    private async Task EnsureJournalAccessByIdAsync(long id)
    {
        var journal = await _journalEntryService.GetByIdAsync(id);
        await EnsureJournalLocationAuthorizedAsync(journal);
    }

    private async Task EnsureJournalLocationAuthorizedAsync(JournalEntryDto journal)
    {
        if (journal.OutletId.HasValue)
        {
            await _outletAccess.ResolveAndAuthorizeLocationAsync(journal.OutletId, "outlet");
            return;
        }

        if (journal.WarehouseId.HasValue)
        {
            await _outletAccess.ResolveAndAuthorizeLocationAsync(journal.WarehouseId, "warehouse");
        }
    }

    private bool TryGetCurrentUserId(out long userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(value, out userId);
    }
}
