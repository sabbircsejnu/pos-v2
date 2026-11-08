using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RetailPOS.API.Authorization;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Exchanges;
using RetailPOS.Application.Exchanges.Abstractions;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Application.Sales.Commands;

namespace RetailPOS.API.Controllers;

public sealed class ExchangeReturnDto
{
    public string SourceKind { get; set; } = ExchangeSourceKinds.SaleItem;
    public long SourceLineId { get; set; }
    public decimal Quantity { get; set; }
    public bool ConditionSellable { get; set; }
    public bool PackagingReusable { get; set; }
}

public sealed class ExchangeReplacementDto
{
    public long VariantId { get; set; }
    public decimal Quantity { get; set; }
    public decimal DiscountAmount { get; set; }
    public long? SellingOptionId { get; set; }
    public long? ExpectedConfigVersionId { get; set; }
    public string? FulfilmentState { get; set; }
    public bool IncludePackaging { get; set; }
}

public class ExchangeRequestDto
{
    public long ReceivingOutletId { get; set; }
    public long? TerminalId { get; set; }
    public List<long> SourceSaleIds { get; set; } = [];
    public List<ExchangeReturnDto> Returns { get; set; } = [];
    public List<ExchangeReplacementDto> Replacements { get; set; } = [];
    public decimal InvoiceDiscount { get; set; }
    public long? SalespersonUserId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? ReasonOtherText { get; set; }
    public string? InternalNote { get; set; }
}

public sealed class ExchangeSettlementDto
{
    public string Direction { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? Tendered { get; set; }
    public string? ExternalReference { get; set; }
    public bool ExternalRefundConfirmed { get; set; }
}

public sealed class ConfirmExchangeDto : ExchangeRequestDto
{
    public string PreviewHash { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public List<ExchangeSettlementDto> Settlements { get; set; } = [];
    public List<string> ApprovalTokens { get; set; } = [];
}

public sealed class ExchangeApprovalDto
{
    public string Type { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class GrantExchangeApprovalsDto
{
    public ExchangeRequestDto Preview { get; set; } = new();
    public string PreviewHash { get; set; } = string.Empty;
    public List<ExchangeApprovalDto> Approvals { get; set; } = [];
    public string SupervisorLogin { get; set; } = string.Empty;
    public string SupervisorPassword { get; set; } = string.Empty;
    public decimal? RefundAmount { get; set; }
    public string? RefundMethod { get; set; }
}

public sealed class ReverseExchangeDto
{
    public string Reason { get; set; } = string.Empty;
    public long? TerminalId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public List<ExchangeSettlementDto> Settlements { get; set; } = [];
}

public sealed class ExchangeReceiptPrintAttemptDto
{
    public long? TerminalId { get; set; }
    public bool Succeeded { get; set; }
}

public sealed class ExchangeReprintDto
{
    public long? TerminalId { get; set; }
}

public sealed class SaveExchangePolicyDto
{
    public int? ExpectedVersionNo { get; set; }
    public bool IsEnabled { get; set; }
    public bool AllowLowerPriceExchange { get; set; }
    public int ExchangeWindowDays { get; set; }
    public bool AllowReplacementReExchange { get; set; }
    public int? ReplacementWindowDays { get; set; }
    public int? MaxExchangeCountPerLineage { get; set; }
    public string PhoneVerificationMode { get; set; } = ExchangePhoneVerificationModes.Optional;
    public decimal? HighValueApprovalThreshold { get; set; }
    public string NegativeTargetAdjustmentMode { get; set; } = ExchangeNegativeTargetModes.TrackOnly;
}

/// <summary>
/// Product exchange (Exchange SRS/LLD v2.x). The cashier, business, prices, VAT, totals, dates and the exchange number
/// always come from the server; the permission flags come from the authenticated principal only.
/// </summary>
[ApiController]
[Route("api/pos/exchanges")]
public sealed class ExchangesController(ISender sender, IUserOutletAccessService outletAccess) : ControllerBase
{
    [HttpGet("policy")]
    [Authorize(Policy = "settings.view")]
    public async Task<ActionResult<ApiResponse<ExchangePolicyReadModel>>> GetPolicy(CancellationToken cancellationToken) =>
        Ok(ApiResponse<ExchangePolicyReadModel>.SuccessResponse(await sender.Send(new GetExchangePolicyQuery(), cancellationToken)));

    [HttpPut("policy")]
    [Authorize(Policy = "settings.edit")]
    public async Task<ActionResult<ApiResponse<ExchangePolicyReadModel>>> SavePolicy([FromBody] SaveExchangePolicyDto dto, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId)
            return Unauthorized(ApiResponse<ExchangePolicyReadModel>.ErrorResponse("User identity not found"));
        var result = await sender.Send(new SaveExchangePolicyCommand(
            dto.ExpectedVersionNo, dto.IsEnabled, dto.AllowLowerPriceExchange, dto.ExchangeWindowDays, dto.AllowReplacementReExchange,
            dto.ReplacementWindowDays, dto.MaxExchangeCountPerLineage, dto.PhoneVerificationMode?.Trim() ?? string.Empty,
            dto.HighValueApprovalThreshold, dto.NegativeTargetAdjustmentMode?.Trim() ?? string.Empty, userId), cancellationToken);
        return Ok(ApiResponse<ExchangePolicyReadModel>.SuccessResponse(result, "Exchange policy saved"));
    }

    [HttpGet("source-invoice")]
    [Authorize(Policy = PermissionCatalog.ExchangeCreate)]
    public async Task<ActionResult<ApiResponse<ExchangeSourceInvoiceReadModel>>> FindSourceInvoice([FromQuery] string invoiceNumber, CancellationToken cancellationToken) =>
        Ok(ApiResponse<ExchangeSourceInvoiceReadModel>.SuccessResponse(
            await sender.Send(new GetExchangeSourceInvoiceQuery(invoiceNumber), cancellationToken)));

    [HttpPost("preview")]
    [Authorize(Policy = PermissionCatalog.ExchangeCreate)]
    public async Task<ActionResult<ApiResponse<ExchangePreviewReadModel>>> Preview([FromBody] ExchangeRequestDto dto, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId)
            return Unauthorized(ApiResponse<ExchangePreviewReadModel>.ErrorResponse("User identity not found"));
        var request = await ToRequestAsync(dto);
        var preview = await sender.Send(new PreviewExchangeQuery(request, Actor(userId)), cancellationToken);
        return Ok(ApiResponse<ExchangePreviewReadModel>.SuccessResponse(preview));
    }

    [HttpPost("approvals")]
    [Authorize(Policy = PermissionCatalog.ExchangeCreate)]
    [EnableRateLimiting("exchange-approval")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ExchangeApprovalTokenReadModel>>>> GrantApprovals(
        [FromBody] GrantExchangeApprovalsDto dto, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId)
            return Unauthorized(ApiResponse<IReadOnlyList<ExchangeApprovalTokenReadModel>>.ErrorResponse("User identity not found"));
        var request = await ToRequestAsync(dto.Preview);
        var tokens = await sender.Send(new GrantExchangeApprovalsCommand(
            request,
            Actor(userId),
            dto.PreviewHash,
            dto.Approvals.Select(item => new ExchangeApprovalItem(item.Type?.Trim() ?? string.Empty, item.Reason)).ToList(),
            dto.SupervisorLogin,
            dto.SupervisorPassword,
            dto.RefundAmount is { } amount && dto.RefundMethod is { } method ? new ExchangeRefundApprovalTerms(amount, method) : null),
            cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ExchangeApprovalTokenReadModel>>.SuccessResponse(tokens, "Approved"));
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.ExchangeCreate)]
    public async Task<ActionResult<ApiResponse<ExchangeResultReadModel>>> Confirm([FromBody] ConfirmExchangeDto dto, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId)
            return Unauthorized(ApiResponse<ExchangeResultReadModel>.ErrorResponse("User identity not found"));
        var request = await ToRequestAsync(dto);
        var result = await sender.Send(new ConfirmExchangeCommand(
            request,
            Actor(userId),
            dto.PreviewHash,
            dto.IdempotencyKey,
            dto.Settlements.Select(item => new ExchangeSettlementInput(item.Direction, item.Method, item.Amount, item.Tendered, item.ExternalReference, item.ExternalRefundConfirmed)).ToList(),
            dto.ApprovalTokens),
            cancellationToken);
        var body = ApiResponse<ExchangeResultReadModel>.SuccessResponse(result, result.Replayed ? "Exchange already completed" : "Exchange completed");
        return result.Replayed ? Ok(body) : StatusCode(StatusCodes.Status201Created, body);
    }

    /// <summary>Exchange history (EX-30, D-11): business and the caller's sales read scope on the receiving outlet.</summary>
    [HttpGet]
    [Authorize(Policy = "sales.view")]
    public async Task<ActionResult<ApiResponse<ExchangeHistoryPageReadModel>>> GetHistory(
        [FromQuery] string? search, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] long? outletId,
        [FromQuery] string? status, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Ok(ApiResponse<ExchangeHistoryPageReadModel>.SuccessResponse(await sender.Send(new GetExchangeHistoryQuery(
            search, from is { } start ? DateOnly.FromDateTime(start) : null, to is { } end ? DateOnly.FromDateTime(end) : null,
            outletId, status, page, pageSize), cancellationToken)));

    /// <summary>Exchange metrics by business date (EX-20, D-06, D-07), within the same read scope as the history.</summary>
    [HttpGet("metrics")]
    [Authorize(Policy = "sales.view")]
    public async Task<ActionResult<ApiResponse<ExchangeMetricsReadModel>>> GetMetrics(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] long? outletId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<ExchangeMetricsReadModel>.SuccessResponse(await sender.Send(new GetExchangeMetricsQuery(
            from is { } start ? DateOnly.FromDateTime(start) : null, to is { } end ? DateOnly.FromDateTime(end) : null, outletId),
            cancellationToken)));

    [HttpGet("{id:long}")]
    [Authorize(Policy = "sales.view")]
    public async Task<ActionResult<ApiResponse<ExchangeDetailReadModel>>> GetDetail(long id, CancellationToken cancellationToken) =>
        Ok(ApiResponse<ExchangeDetailReadModel>.SuccessResponse(await sender.Send(new GetExchangeDetailQuery(id), cancellationToken)));

    [HttpGet("by-sale/{saleId:long}")]
    [Authorize(Policy = "sales.view")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ExchangeHistoryItemReadModel>>>> GetBySale(long saleId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<IReadOnlyList<ExchangeHistoryItemReadModel>>.SuccessResponse(await sender.Send(new GetExchangesBySaleQuery(saleId), cancellationToken)));

    [HttpGet("{id:long}/receipt-document")]
    [Authorize(Policy = "sales.view")]
    public async Task<ActionResult<ApiResponse<ExchangeReceiptDocumentReadModel>>> GetReceiptDocument(long id, CancellationToken cancellationToken) =>
        Ok(ApiResponse<ExchangeReceiptDocumentReadModel>.SuccessResponse(await sender.Send(new GetExchangeReceiptDocumentQuery(id), cancellationToken)));

    [HttpGet("{id:long}/receipt-pdf")]
    [Authorize(Policy = "sales.view")]
    public async Task<IActionResult> GetReceiptPdf(long id, [FromQuery] string? mode, CancellationToken cancellationToken)
    {
        var pdf = await sender.Send(new GetExchangeReceiptPdfQuery(id, mode), cancellationToken);
        return File(pdf.Content, "application/pdf", $"exchange-{pdf.ExchangeNumber}.pdf");
    }

    [HttpPost("{id:long}/reprint")]
    [Authorize(Policy = "sales.view")]
    public async Task<ActionResult<ApiResponse<object>>> Reprint(long id, [FromBody] ExchangeReprintDto? dto, CancellationToken cancellationToken)
    {
        await sender.Send(new RecordExchangeReceiptPrintCommand(id, dto?.TerminalId, ExchangePrintActions.Reprint, CurrentUserId()), cancellationToken);
        return Ok(ApiResponse<object>.SuccessResponse("Reprint recorded."));
    }

    [HttpPost("{id:long}/receipt-print-attempt")]
    [Authorize(Policy = "sales.view")]
    public async Task<ActionResult<ApiResponse<object>>> RecordPrintAttempt(long id, [FromBody] ExchangeReceiptPrintAttemptDto dto, CancellationToken cancellationToken)
    {
        await sender.Send(new RecordExchangeReceiptPrintCommand(
            id, dto.TerminalId, dto.Succeeded ? ExchangePrintActions.Print : ExchangePrintActions.PrintAttemptFailed, CurrentUserId()), cancellationToken);
        return Ok(ApiResponse<object>.SuccessResponse("Receipt attempt recorded."));
    }

    [HttpGet("{id:long}/reversal-eligibility")]
    [Authorize(Policy = PermissionCatalog.ExchangeReverse)]
    public async Task<ActionResult<ApiResponse<ExchangeReversalEligibilityReadModel>>> GetReversalEligibility(long id, CancellationToken cancellationToken) =>
        Ok(ApiResponse<ExchangeReversalEligibilityReadModel>.SuccessResponse(await sender.Send(new GetExchangeReversalEligibilityQuery(id), cancellationToken)));

    /// <summary>Controlled whole reversal (EX-14, C-05): compensating effects only, never edits of the original.</summary>
    [HttpPost("{id:long}/reverse")]
    [Authorize(Policy = PermissionCatalog.ExchangeReverse)]
    public async Task<ActionResult<ApiResponse<ExchangeReversalResultReadModel>>> Reverse(long id, [FromBody] ReverseExchangeDto dto, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId)
            return Unauthorized(ApiResponse<ExchangeReversalResultReadModel>.ErrorResponse("User identity not found"));
        var result = await sender.Send(new ReverseExchangeCommand(
            id,
            dto.Reason,
            dto.TerminalId,
            dto.IdempotencyKey,
            dto.Settlements.Select(item => new ExchangeSettlementInput(item.Direction, item.Method, item.Amount, item.Tendered, item.ExternalReference, item.ExternalRefundConfirmed)).ToList(),
            userId), cancellationToken);
        var body = ApiResponse<ExchangeReversalResultReadModel>.SuccessResponse(result, result.Replayed ? "Exchange already reversed" : "Exchange reversed");
        return result.Replayed ? Ok(body) : StatusCode(StatusCodes.Status201Created, body);
    }

    private async Task<ExchangeRequest> ToRequestAsync(ExchangeRequestDto dto)
    {
        // The receiving outlet is enforced exactly like a sale's outlet: never trusted from the client (EX-06, C-08).
        var outletId = await outletAccess.EnforceWriteOutletAsync(dto.ReceivingOutletId);
        return new ExchangeRequest(
            outletId,
            dto.TerminalId,
            dto.SourceSaleIds,
            dto.Returns.Select(line => new ExchangeReturnInput(line.SourceKind?.Trim() ?? string.Empty, line.SourceLineId, line.Quantity, line.ConditionSellable, line.PackagingReusable)).ToList(),
            dto.Replacements.Select(line => new CreateSaleLine(
                line.VariantId, line.Quantity, 0m, line.DiscountAmount, null, null, line.SellingOptionId, line.ExpectedConfigVersionId,
                line.FulfilmentState, line.IncludePackaging)).ToList(),
            dto.InvoiceDiscount,
            dto.SalespersonUserId,
            dto.ReasonCode?.Trim() ?? string.Empty,
            dto.ReasonOtherText,
            dto.InternalNote);
    }

    private ExchangeActor Actor(long userId) => new(
        userId,
        User.HasPermission("sales.discount.apply"),
        User.HasPermission("sales.discount.override_limit"),
        User.HasPermission(PermissionCatalog.ExchangeRefund));

    private long? CurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return long.TryParse(claim, out var id) && id > 0 ? id : null;
    }
}
