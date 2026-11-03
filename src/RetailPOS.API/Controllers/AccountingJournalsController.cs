using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.AccountingJournals.Queries;
using RetailPOS.Application.AccountingJournals.ReadModels;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/journals")]
public class AccountingJournalsController : ControllerBase
{
    private readonly IUserOutletAccessService _outletAccess;
    private readonly ITenantAccessService _tenantAccess;
    private readonly IRoleSwitchContext _roleSwitchContext;
    private readonly ISender _sender;

    public AccountingJournalsController(
        IUserOutletAccessService outletAccess,
        ITenantAccessService tenantAccess,
        IRoleSwitchContext roleSwitchContext,
        ISender sender)
    {
        _outletAccess = outletAccess;
        _tenantAccess = tenantAccess;
        _roleSwitchContext = roleSwitchContext;
        _sender = sender;
    }

    [HttpGet]
    [Authorize(Policy = "accounting.journals.view")]
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
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        outletId = await _outletAccess.ResolveAndAuthorizeOutletFilterAsync(outletId);
        var businessId = _tenantAccess.RequireBusinessId();
        var result = await _sender.Send(
            new SearchJournalEntriesQuery(new JournalSearchFilter(
                businessId,
                postingDateStart,
                postingDateEnd,
                businessDateStart,
                businessDateEnd,
                status,
                outletId,
                journalType,
                journalNumber,
                accountId,
                referenceType,
                referenceNumber,
                createdBy,
                postedBy,
                sortBy,
                sortDirection,
                pageNumber,
                pageSize)),
            cancellationToken);

        return Ok(ApiResponse<JournalListDto>.SuccessResponse(JournalEntryReadModelMapper.ToDto(result)));
    }

    [HttpGet("{id:long}")]
    [Authorize(Policy = "accounting.journals.view")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await GetJournalEntryAsync(id, cancellationToken);
        await EnsureJournalLocationAuthorizedAsync(result);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result));
    }

    [HttpPost]
    [Authorize(Policy = "accounting.manual_journals.create")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> Create(
        [FromBody] CreateJournalEntryDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        dto.OutletId = await _outletAccess.EnforceWriteOutletAsync(dto.OutletId);
        var businessId = _tenantAccess.RequireBusinessId();
        var createdId = await _sender.Send(new CreateManualJournalDraftCommand(
            businessId,
            userId,
            new ManualJournalDraftInput(
                dto.JournalType,
                dto.TransactionDate,
                dto.PostingDate,
                dto.BusinessDate,
                dto.ReferenceType,
                dto.ReferenceId,
                dto.ReferenceNumber,
                dto.Description,
                dto.OutletId,
                dto.WarehouseId,
                dto.TerminalId,
                dto.CashSessionId,
                MapLines(dto.Lines))),
            cancellationToken);
        var result = await GetJournalEntryAsync(createdId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal draft created successfully"));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "accounting.manual_journals.update")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> UpdateDraft(
        long id,
        [FromBody] UpdateJournalEntryDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id, cancellationToken);
        dto.OutletId = await _outletAccess.EnforceWriteOutletAsync(dto.OutletId);

        var businessId = _tenantAccess.RequireBusinessId();
        var updatedId = await _sender.Send(new UpdateManualJournalDraftCommand(
            businessId,
            userId,
            id,
            new ManualJournalDraftUpdateInput(
                dto.JournalType,
                dto.TransactionDate,
                dto.PostingDate,
                dto.BusinessDate,
                dto.ReferenceType,
                dto.ReferenceId,
                dto.ReferenceNumber,
                dto.Description,
                dto.OutletId,
                dto.WarehouseId,
                dto.TerminalId,
                dto.CashSessionId,
                MapLines(dto.Lines))),
            cancellationToken);
        var result = await GetJournalEntryAsync(updatedId, cancellationToken);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal draft updated successfully"));
    }

    [HttpPost("{id:long}/submit")]
    [Authorize(Policy = "accounting.manual_journals.submit")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> Submit(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id, cancellationToken);

        var businessId = _tenantAccess.RequireBusinessId();
        var submittedId = await _sender.Send(
            new SubmitManualJournalCommand(businessId, userId, id),
            cancellationToken);
        var result = await GetJournalEntryAsync(submittedId, cancellationToken);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal submitted successfully"));
    }

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = "accounting.manual_journals.approve")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> Approve(
        long id,
        [FromBody] ApproveJournalEntryDto? dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id, cancellationToken);

        var businessId = _tenantAccess.RequireBusinessId();
        var approvedId = await _sender.Send(new ApproveManualJournalCommand(
            businessId,
            userId,
            id,
            _roleSwitchContext.IsBusinessOwner || _roleSwitchContext.IsSuperAdmin),
            cancellationToken);
        var result = await GetJournalEntryAsync(approvedId, cancellationToken);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal approved successfully"));
    }

    [HttpPost("{id:long}/reject")]
    [Authorize(Policy = "accounting.manual_journals.reject")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> Reject(
        long id,
        [FromBody] RequiredReasonActionDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id, cancellationToken);

        var businessId = _tenantAccess.RequireBusinessId();
        var rejectedId = await _sender.Send(
            new RejectManualJournalCommand(businessId, userId, id, dto.Reason),
            cancellationToken);
        var result = await GetJournalEntryAsync(rejectedId, cancellationToken);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal rejected successfully"));
    }

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = "accounting.manual_journals.cancel")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> CancelDraft(
        long id,
        [FromBody] CancelJournalEntryDto? dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id, cancellationToken);

        var businessId = _tenantAccess.RequireBusinessId();
        var cancelledId = await _sender.Send(
            new CancelManualJournalCommand(businessId, userId, id, dto?.Reason),
            cancellationToken);
        var result = await GetJournalEntryAsync(cancelledId, cancellationToken);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal draft cancelled successfully"));
    }

    [HttpPost("{id:long}/post")]
    [Authorize(Policy = "accounting.manual_journals.post")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> Post(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id, cancellationToken);

        var businessId = _tenantAccess.RequireBusinessId();
        var postedId = await _sender.Send(
            new PostJournalEntryCommand(businessId, userId, id),
            cancellationToken);
        var result = await GetJournalEntryAsync(postedId, cancellationToken);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal posted successfully"));
    }

    [HttpPost("{id:long}/reverse")]
    [Authorize(Policy = "accounting.manual_journals.reverse")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> Reverse(
        long id,
        [FromBody] ReverseJournalEntryDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id, cancellationToken);

        var businessId = _tenantAccess.RequireBusinessId();
        var reversalId = await _sender.Send(
            new ReverseJournalEntryCommand(
                businessId,
                userId,
                id,
                new ManualJournalReversalInput(dto.Reason, dto.PostingDate, dto.BusinessDate)),
            cancellationToken);
        var result = await GetJournalEntryAsync(reversalId, cancellationToken);
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(result, "Journal reversed successfully"));
    }

    [HttpGet("{id:long}/attachments")]
    [Authorize(Policy = "accounting.manual_journals.view")]
    public async Task<ActionResult<ApiResponse<List<JournalEntryAttachmentDto>>>> GetAttachments(
        long id,
        CancellationToken cancellationToken)
    {
        await EnsureJournalAccessByIdAsync(id, cancellationToken);
        var result = await _sender.Send(new GetJournalAttachmentsQuery(id), cancellationToken);
        var response = result.Select(MapJournalAttachment).ToList();
        return Ok(ApiResponse<List<JournalEntryAttachmentDto>>.SuccessResponse(response));
    }

    [HttpPost("{id:long}/attachments")]
    [Authorize(Policy = "accounting.manual_journals.update")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<JournalEntryAttachmentDto>>> UploadAttachment(
        long id,
        [FromForm] UploadJournalAttachmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<JournalEntryAttachmentDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id, cancellationToken);
        var file = request.File;
        using var content = file?.OpenReadStream();
        var result = await _sender.Send(
            new UploadJournalAttachmentCommand(
                id,
                content,
                file?.FileName,
                file?.ContentType,
                file?.Length ?? 0,
                userId),
            cancellationToken);
        return Ok(ApiResponse<JournalEntryAttachmentDto>.SuccessResponse(
            MapJournalAttachment(result),
            "Attachment uploaded successfully"));
    }

    [HttpGet("{id:long}/attachments/{attachmentId:long}/download")]
    [Authorize(Policy = "accounting.manual_journals.view")]
    public async Task<IActionResult> DownloadAttachment(
        long id,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out _))
        {
            return Unauthorized(ApiResponse<JournalEntryAttachmentDto>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id, cancellationToken);
        var result = await _sender.Send(
            new DownloadJournalAttachmentQuery(id, attachmentId),
            cancellationToken);
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpDelete("{id:long}/attachments/{attachmentId:long}")]
    [Authorize(Policy = "accounting.manual_journals.update")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteAttachment(
        long id,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out _))
        {
            return Unauthorized(ApiResponse<object>.ErrorResponse("User identity not found"));
        }

        await EnsureJournalAccessByIdAsync(id, cancellationToken);
        await _sender.Send(
            new DeleteJournalAttachmentCommand(id, attachmentId),
            cancellationToken);
        return Ok(ApiResponse<object>.SuccessResponse("Attachment deleted successfully"));
    }

    private static JournalEntryAttachmentDto MapJournalAttachment(
        JournalAttachmentReadModel attachment) =>
        new()
        {
            Id = attachment.Id,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            FileSizeBytes = attachment.FileSizeBytes,
            CreatedAt = attachment.CreatedAt,
            CreatedBy = attachment.CreatedBy
        };

    public sealed class UploadJournalAttachmentRequest
    {
        public IFormFile File { get; set; } = default!;
    }

    private async Task EnsureJournalAccessByIdAsync(long id, CancellationToken cancellationToken)
    {
        var journal = await GetJournalEntryAsync(id, cancellationToken);
        await EnsureJournalLocationAuthorizedAsync(journal);
    }

    private async Task<JournalEntryDto> GetJournalEntryAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var businessId = _tenantAccess.RequireBusinessId();
        var result = await _sender.Send(
            new GetJournalEntryByIdQuery(businessId, id),
            cancellationToken);
        return JournalEntryReadModelMapper.ToDto(result);
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

    private static List<ManualJournalDraftLineInput> MapLines(
        IEnumerable<CreateJournalEntryLineDto>? lines) =>
        (lines ?? []).Select(line => new ManualJournalDraftLineInput(
            line.AccountId,
            line.Description,
            line.DebitAmount,
            line.CreditAmount,
            line.OutletId,
            line.WarehouseId,
            line.SupplierId,
            line.CustomerId,
            line.PaymentMethodId,
            line.CostCenterId)).ToList();
}
