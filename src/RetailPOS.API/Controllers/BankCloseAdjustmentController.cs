using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.Commands;
using RetailPOS.Application.BankReconciliation.Queries;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/reconciliation")]
[Authorize]
public sealed class BankCloseAdjustmentController : ControllerBase
{
    private readonly ISender _sender;
    public BankCloseAdjustmentController(ISender sender) => _sender = sender;
    [HttpGet("bank-accounts")][Authorize(Policy = "accounting.reconciliation.view")]
    public async Task<ActionResult<ApiResponse<List<BankAccountDto>>>> BankAccounts(CancellationToken cancellationToken)
    {
        var accounts = await _sender.Send(new GetActiveBankAccountsQuery(), cancellationToken);
        return Ok(ApiResponse<List<BankAccountDto>>.SuccessResponse(accounts.Select(Map).ToList()));
    }
    [HttpGet("matches")][Authorize(Policy = "accounting.reconciliation.view")]
    public async Task<ActionResult<ApiResponse<List<ReconciliationMatchDetailDto>>>> Matches(
        [FromQuery] long sessionId,
        CancellationToken cancellationToken)
    {
        var matches = await _sender.Send(new GetReconciliationMatchesQuery(sessionId), cancellationToken);
        return Ok(ApiResponse<List<ReconciliationMatchDetailDto>>.SuccessResponse(matches.Select(Map).ToList()));
    }
    [HttpGet("adjustments")][Authorize(Policy = "accounting.reconciliation.view")]
    public async Task<ActionResult<ApiResponse<List<StatementAdjustmentDto>>>> Adjustments(
        [FromQuery] long bankAccountId,
        CancellationToken cancellationToken)
    {
        var adjustments = await _sender.Send(
            new GetStatementAdjustmentsQuery(bankAccountId),
            cancellationToken);
        return Ok(ApiResponse<List<StatementAdjustmentDto>>.SuccessResponse(adjustments.Select(Map).ToList()));
    }
    [HttpGet("exceptions")][Authorize(Policy = "accounting.reconciliation.view")]
    public async Task<ActionResult<ApiResponse<List<StatementExceptionDto>>>> Exceptions(
        [FromQuery] long bankAccountId,
        CancellationToken cancellationToken)
    {
        var exceptions = await _sender.Send(
            new GetStatementExceptionsQuery(bankAccountId),
            cancellationToken);
        return Ok(ApiResponse<List<StatementExceptionDto>>.SuccessResponse(exceptions.Select(Map).ToList()));
    }
    [HttpPost("sessions/{id:long}/prepare-close")][Authorize(Policy = "accounting.reconciliation.close")]
    public async Task<ActionResult<ApiResponse<BankReconciliationSessionDto>>> Prepare(
        long id,
        CancellationToken cancellationToken)
    {
        var session = await _sender.Send(
            new PrepareBankReconciliationForCloseCommand(UserId(), id),
            cancellationToken);
        return Ok(ApiResponse<BankReconciliationSessionDto>.SuccessResponse(Map(session)));
    }
    [HttpPost("sessions/{id:long}/close")][Authorize(Policy = "accounting.reconciliation.close")]
    public async Task<ActionResult<ApiResponse<BankReconciliationSessionDto>>> Close(
        long id,
        CloseReconciliationDto dto,
        CancellationToken cancellationToken)
    {
        var session = await _sender.Send(
            new CloseBankReconciliationCommand(UserId(), id, dto.Reason),
            cancellationToken);
        return Ok(ApiResponse<BankReconciliationSessionDto>.SuccessResponse(Map(session)));
    }
    [HttpPost("sessions/{id:long}/reopen")][Authorize(Policy = "accounting.reconciliation.reopen")]
    public async Task<ActionResult<ApiResponse<BankReconciliationSessionDto>>> Reopen(
        long id,
        ReopenReconciliationDto dto,
        CancellationToken cancellationToken)
    {
        var session = await _sender.Send(
            new ReopenBankReconciliationCommand(UserId(), id, dto.Reason),
            cancellationToken);
        return Ok(ApiResponse<BankReconciliationSessionDto>.SuccessResponse(Map(session)));
    }
    [HttpPost("adjustments")][Authorize(Policy = "accounting.adjustments.propose")]
    public async Task<ActionResult<ApiResponse<StatementAdjustmentDto>>> Propose(
        CreateStatementAdjustmentDto dto,
        CancellationToken cancellationToken)
    {
        var adjustment = await _sender.Send(
            new ProposeBankStatementAdjustmentCommand(
                UserId(),
                new ProposeBankStatementAdjustmentInput(
                    dto.BankStatementLineId,
                    dto.AdjustmentType,
                    dto.Amount,
                    dto.Reason,
                    dto.IdempotencyKey)),
            cancellationToken);
        return Ok(ApiResponse<StatementAdjustmentDto>.SuccessResponse(Map(adjustment)));
    }
    [HttpPost("adjustments/{id:long}/approve")][Authorize(Policy = "accounting.adjustments.approve")]
    public async Task<ActionResult<ApiResponse<StatementAdjustmentDto>>> Approve(
        long id,
        ApproveStatementAdjustmentDto dto,
        [FromQuery] DateTime postingDate,
        CancellationToken cancellationToken)
    {
        var adjustment = await _sender.Send(
            new ApproveBankStatementAdjustmentCommand(
                UserId(),
                id,
                postingDate),
            cancellationToken);
        return Ok(ApiResponse<StatementAdjustmentDto>.SuccessResponse(Map(adjustment)));
    }
    [HttpPost("adjustments/{id:long}/reverse")][Authorize(Policy = "accounting.adjustments.reverse")]
    public async Task<ActionResult<ApiResponse<StatementAdjustmentDto>>> Reverse(
        long id,
        ReverseStatementAdjustmentDto dto,
        CancellationToken cancellationToken)
    {
        var adjustment = await _sender.Send(
            new ReverseBankStatementAdjustmentCommand(
                UserId(),
                id,
                dto.Reason,
                dto.PostingDate),
            cancellationToken);
        return Ok(ApiResponse<StatementAdjustmentDto>.SuccessResponse(Map(adjustment)));
    }
    [HttpPost("exceptions")][Authorize(Policy = "accounting.exceptions.manage")]
    public async Task<ActionResult> CreateException(
        CreateStatementExceptionDto dto,
        [FromQuery] long statementLineId,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new CreateBankStatementExceptionCommand(
                statementLineId,
                dto.Classification,
                dto.Reason,
                UserId()),
            cancellationToken);
        return Ok();
    }
    [HttpPost("exceptions/{id:long}/resolve")][Authorize(Policy = "accounting.exceptions.manage")]
    public async Task<ActionResult> ResolveException(
        long id,
        ResolveStatementExceptionDto dto,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ResolveBankStatementExceptionCommand(id, dto.ResolutionNotes, UserId()),
            cancellationToken);
        return Ok();
    }
    private long UserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException("User identity not found.");
    private static BankAccountDto Map(BankAccountReadModel model) => new() { Id = model.Id, DisplayName = model.DisplayName, ChartAccountId = model.ChartAccountId, OutletId = model.OutletId, IsActive = model.IsActive };
    private static ReconciliationMatchDetailDto Map(ReconciliationMatchReadModel model) => new() { Id = model.Id, SessionId = model.SessionId, Status = model.Status, StatementTotal = model.StatementTotal, GlTotal = model.GlTotal, StatementLineIds = model.StatementLineIds.ToList(), JournalEntryLineIds = model.JournalEntryLineIds.ToList() };
    private static StatementAdjustmentDto Map(StatementAdjustmentReadModel model) => new() { Id = model.Id, BankStatementLineId = model.BankStatementLineId, AdjustmentType = model.AdjustmentType, Amount = model.Amount, Status = model.Status, JournalEntryId = model.JournalEntryId, ReversalJournalEntryId = model.ReversalJournalEntryId };
    private static StatementExceptionDto Map(StatementExceptionReadModel model) => new() { Id = model.Id, BankStatementLineId = model.BankStatementLineId, Classification = model.Classification, Status = model.Status, Reason = model.Reason, ResolutionNotes = model.ResolutionNotes, CreatedBy = model.CreatedBy, CreatedAt = model.CreatedAt, ResolvedBy = model.ResolvedBy, ResolvedAt = model.ResolvedAt };
    private static BankReconciliationSessionDto Map(BankReconciliationSessionReadModel model) => new() { Id = model.Id, BusinessId = model.BusinessId, BankAccountId = model.BankAccountId, PeriodStart = model.PeriodStart, PeriodEnd = model.PeriodEnd, StatementOpeningBalance = model.StatementOpeningBalance, StatementClosingBalance = model.StatementClosingBalance, GlOpeningBalance = model.GlOpeningBalance, Status = model.Status };
}
