using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Pos;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Commands;
using RetailPOS.Application.Pos.Queries;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Controllers;

/// <summary>
/// Cash Session management (Phase 3). Every action carries its own granular permission; the handlers additionally
/// enforce the Business/Outlet scope, so a client-side route guard is never the only control.
/// </summary>
[ApiController]
[Route("api/cash-sessions")]
[Authorize]
public sealed class CashSessionsController(
    ISender sender,
    IUserOutletAccessService outletAccess,
    IBusinessScope businessScope,
    ILogger<CashSessionsController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "cash_sessions.view")]
    public async Task<ActionResult<ApiResponse<CashSessionListPage>>> List(
        [FromQuery] long outletId,
        [FromQuery] long? terminalId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? state,
        [FromQuery] long? openedByUserId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new ListCashSessionsQuery(outletId, terminalId, from, to, state, openedByUserId, page, pageSize),
            cancellationToken);
        return Ok(ApiResponse<CashSessionListPage>.SuccessResponse(result));
    }

    [HttpGet("open-context")]
    [Authorize(Policy = "cash_sessions.view")]
    public async Task<ActionResult<ApiResponse<CashSessionOpenContextReadModel>>> OpenContext(
        [FromQuery] long outletId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCashSessionOpenContextQuery(outletId), cancellationToken);
        return Ok(ApiResponse<CashSessionOpenContextReadModel>.SuccessResponse(result));
    }

    [HttpGet("business-day-sales")]
    [Authorize(Policy = "cash_sessions.view")]
    public async Task<ActionResult<ApiResponse<BusinessDaySalesReadModel>>> BusinessDaySales(
        [FromQuery] long outletId,
        [FromQuery] DateTime? businessDate,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetBusinessDaySalesQuery(outletId, businessDate), cancellationToken);
        return Ok(ApiResponse<BusinessDaySalesReadModel>.SuccessResponse(result));
    }

    [HttpGet("active")]
    [Authorize(Policy = "cash_sessions.view")]
    public async Task<ActionResult<ApiResponse<CashSessionDto?>>> Active(
        [FromQuery] long outletId,
        [FromQuery] long terminalId,
        CancellationToken cancellationToken)
    {
        var active = await sender.Send(new GetActiveCashSessionQuery(outletId, terminalId), cancellationToken);
        return Ok(active is null
            ? ApiResponse<CashSessionDto?>.SuccessResponse(null, "No active cash session.")
            : ApiResponse<CashSessionDto?>.SuccessResponse(CashSessionApiMapper.ToDto(active)));
    }

    [HttpGet("{id:long}")]
    [Authorize(Policy = "cash_sessions.view")]
    public async Task<ActionResult<ApiResponse<CashSessionDetailReadModel>>> Detail(long id, CancellationToken cancellationToken)
    {
        var detail = await sender.Send(new GetCashSessionDetailQuery(id), cancellationToken);
        return Ok(ApiResponse<CashSessionDetailReadModel>.SuccessResponse(detail));
    }

    /// <summary>Closing summary PDF (FR-CS-05) from the immutable close snapshot; 409 while the session is still open.</summary>
    [HttpGet("{id:long}/closing-summary.pdf")]
    [Authorize(Policy = "cash_sessions.view")]
    public async Task<IActionResult> ClosingSummaryPdf(
        long id,
        [FromServices] ICashSessionCloseSummaryPdfRenderer renderer,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var detail = await sender.Send(new GetCashSessionDetailQuery(id), cancellationToken);
        if (detail.CloseSnapshot is null)
            return Conflict(ApiResponse<object>.ErrorResponse("The closing summary is available once the session is closed."));

        var pdf = renderer.Render(detail, clock.GetUtcNow().UtcDateTime);
        return File(pdf, "application/pdf", $"cash-session-{id}-closing-summary.pdf");
    }

    [HttpGet("{id:long}/summary")]
    [Authorize(Policy = "cash_sessions.view")]
    public async Task<ActionResult<ApiResponse<CashSessionCloseSummaryDto>>> Summary(long id, CancellationToken cancellationToken)
    {
        var summary = await sender.Send(new GetCashSessionCloseSummaryQuery(id), cancellationToken);
        return Ok(ApiResponse<CashSessionCloseSummaryDto>.SuccessResponse(CashSessionApiMapper.ToDto(summary)));
    }

    [HttpPost("open")]
    [Authorize(Policy = "cash_sessions.open")]
    public async Task<ActionResult<ApiResponse<CashSessionDto>>> Open(
        [FromBody] OpenCashSessionDto dto,
        CancellationToken cancellationToken)
    {
        var userId = CashSessionApiMapper.ResolveUserId(User);
        if (userId is null)
            return Unauthorized(ApiResponse<CashSessionDto>.ErrorResponse("Unable to resolve user context."));

        var outletId = await outletAccess.EnforceWriteOutletAsync(dto.OutletId);
        var session = await sender.Send(CashSessionApiMapper.ToCommand(dto, outletId, userId.Value), cancellationToken);
        return Ok(ApiResponse<CashSessionDto>.SuccessResponse(CashSessionApiMapper.ToDto(session), "Cash session opened successfully."));
    }

    [HttpPost("{id:long}/close")]
    [Authorize(Policy = "cash_sessions.close")]
    public async Task<ActionResult<ApiResponse<CashSessionDto>>> Close(
        long id,
        [FromBody] CloseCashSessionDto dto,
        CancellationToken cancellationToken)
    {
        var userId = CashSessionApiMapper.ResolveUserId(User);
        if (userId is null)
            return Unauthorized(ApiResponse<CashSessionDto>.ErrorResponse("Unable to resolve user context."));

        var outletId = await sender.Send(new GetCashSessionOutletIdQuery(id), cancellationToken);
        await outletAccess.ResolveAndAuthorizeLocationAsync(outletId, "outlet");
        var session = await sender.Send(CashSessionApiMapper.ToCommand(id, dto, userId.Value), cancellationToken);
        return Ok(ApiResponse<CashSessionDto>.SuccessResponse(CashSessionApiMapper.ToDto(session), "Cash session closed successfully."));
    }

    [HttpPost("{id:long}/handovers/{handoverId:long}/confirm")]
    [Authorize(Policy = "cash_sessions.handover.confirm")]
    public async Task<ActionResult<ApiResponse<CashSessionHandoverReadModel>>> ConfirmHandover(
        long id,
        long handoverId,
        CancellationToken cancellationToken)
    {
        var userId = CashSessionApiMapper.ResolveUserId(User);
        if (userId is null)
            return Unauthorized(ApiResponse<CashSessionHandoverReadModel>.ErrorResponse("Unable to resolve user context."));

        var handover = await sender.Send(new ConfirmCashHandoverCommand(id, handoverId, userId.Value), cancellationToken);
        return Ok(ApiResponse<CashSessionHandoverReadModel>.SuccessResponse(
            new CashSessionHandoverReadModel(
                handover.Id, handover.Method, handover.Amount, handover.Status, handover.GivenByUserId, null,
                handover.RecipientUserId, handover.RecipientName, handover.BankName, handover.BankAccountReference,
                handover.DepositReference, handover.DepositDate, handover.Notes, handover.CreatedAtUtc,
                handover.ConfirmedAtUtc, handover.ConfirmedByUserId),
            "Handover confirmed."));
    }

    [HttpPost("{id:long}/attachments")]
    [Authorize(Policy = "cash_sessions.attachments.upload")]
    [RequestSizeLimit(CashSessionAttachmentRules.MaxBytes + 64 * 1024)]
    public async Task<ActionResult<ApiResponse<CashSessionAttachmentReadModel>>> UploadAttachment(
        long id,
        IFormFile file,
        [FromQuery] long? handoverId,
        CancellationToken cancellationToken)
    {
        var userId = CashSessionApiMapper.ResolveUserId(User);
        if (userId is null)
            return Unauthorized(ApiResponse<CashSessionAttachmentReadModel>.ErrorResponse("Unable to resolve user context."));
        if (file is null || file.Length == 0 || file.Length > CashSessionAttachmentRules.MaxBytes)
            return BadRequest(ApiResponse<CashSessionAttachmentReadModel>.ErrorResponse("Attach a file of at most 5 MB."));

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        var attachment = await sender.Send(
            new UploadCashSessionAttachmentCommand(id, handoverId, file.FileName, buffer.ToArray(), userId.Value),
            cancellationToken);
        return Ok(ApiResponse<CashSessionAttachmentReadModel>.SuccessResponse(
            new CashSessionAttachmentReadModel(
                attachment.Id, attachment.HandoverId, attachment.FileName, attachment.ContentType, attachment.SizeBytes,
                attachment.Sha256, attachment.UploadedByUserId, attachment.UploadedAtUtc),
            "Attachment added."));
    }

    [HttpGet("{id:long}/attachments/{attachmentId:long}")]
    [Authorize(Policy = "cash_sessions.attachments.download")]
    public async Task<IActionResult> DownloadAttachment(long id, long attachmentId, CancellationToken cancellationToken)
    {
        var content = await sender.Send(new DownloadCashSessionAttachmentQuery(id, attachmentId), cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return File(content.Bytes, content.ContentType, content.FileName);
    }

    [HttpGet("variances")]
    [Authorize(Policy = "cash_sessions.variance.view")]
    public async Task<ActionResult<ApiResponse<CashDifferenceListPage>>> Variances(
        [FromQuery] long outletId,
        [FromQuery] string? status,
        [FromQuery] string? postingStatus,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new ListCashDifferencesQuery(outletId, status, postingStatus, page, pageSize),
            cancellationToken);
        return Ok(ApiResponse<CashDifferenceListPage>.SuccessResponse(result));
    }

    public sealed class ReviewCashDifferenceDto
    {
        public bool Approve { get; set; }
        public string? Notes { get; set; }
    }

    [HttpPost("variances/{approvalId:long}/review")]
    [Authorize(Policy = "cash_sessions.variance.approve")]
    public async Task<ActionResult<ApiResponse<CashDifferenceApprovalReadModel>>> ReviewVariance(
        long approvalId,
        [FromBody] ReviewCashDifferenceDto dto,
        CancellationToken cancellationToken)
    {
        var userId = CashSessionApiMapper.ResolveUserId(User);
        if (userId is null)
            return Unauthorized(ApiResponse<CashDifferenceApprovalReadModel>.ErrorResponse("Unable to resolve user context."));

        var decision = await sender.Send(
            new ReviewCashDifferenceCommand(approvalId, dto.Approve, dto.Notes, userId.Value),
            cancellationToken);
        var approval = decision.Approval;

        // The decision is committed. Posting is a separate unit of work: its failure never changes the decision,
        // the closed session or this response — the row simply shows Failed and can be retried.
        if (dto.Approve && approval.PostingStatus != CashDifferencePostingStatuses.Posted)
        {
            try
            {
                approval = (await sender.Send(new PostCashDifferenceCommand(approvalId, userId.Value), cancellationToken)).Approval;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Cash difference {ApprovalId} was approved but its posting attempt failed", approvalId);
            }
        }

        return Ok(ApiResponse<CashDifferenceApprovalReadModel>.SuccessResponse(
            CashSessionMapper.ToApprovalModel(approval),
            dto.Approve ? "Cash difference approved." : "Cash difference rejected."));
    }

    [HttpPost("variances/{approvalId:long}/post")]
    [Authorize(Policy = "cash_sessions.variance.retry_posting")]
    public async Task<ActionResult<ApiResponse<CashDifferenceApprovalReadModel>>> RetryPosting(
        long approvalId,
        CancellationToken cancellationToken)
    {
        var userId = CashSessionApiMapper.ResolveUserId(User);
        if (userId is null)
            return Unauthorized(ApiResponse<CashDifferenceApprovalReadModel>.ErrorResponse("Unable to resolve user context."));

        var existing = await sender.Send(new GetCashDifferenceQuery(approvalId), cancellationToken);
        var result = await sender.Send(new PostCashDifferenceCommand(existing.Id, userId.Value), cancellationToken);
        return Ok(ApiResponse<CashDifferenceApprovalReadModel>.SuccessResponse(CashSessionMapper.ToApprovalModel(result.Approval)));
    }

    [HttpPost("variances/process-pending")]
    [Authorize(Policy = "cash_sessions.variance.retry_posting")]
    public async Task<ActionResult<ApiResponse<int>>> ProcessPending(
        [FromQuery] long outletId,
        CancellationToken cancellationToken)
    {
        var userId = CashSessionApiMapper.ResolveUserId(User);
        if (userId is null)
            return Unauthorized(ApiResponse<int>.ErrorResponse("Unable to resolve user context."));

        await outletAccess.ResolveAndAuthorizeLocationAsync(outletId, "outlet");
        var processed = await sender.Send(
            new ProcessDueCashDifferencePostingsCommand(businessScope.RequireBusinessId(), outletId, userId.Value),
            cancellationToken);
        return Ok(ApiResponse<int>.SuccessResponse(processed));
    }
}
