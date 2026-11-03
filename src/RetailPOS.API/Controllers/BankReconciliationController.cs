using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.BankReconciliation.Commands;
using RetailPOS.Application.BankReconciliation.Queries;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/reconciliation")]
[Authorize]
public sealed class BankReconciliationController : ControllerBase
{
    private readonly ISender _sender;
    public BankReconciliationController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("sessions")]
    [Authorize(Policy = "accounting.reconciliation.create")]
    public async Task<ActionResult<ApiResponse<BankReconciliationSessionDto>>> Create(
        CreateBankReconciliationSessionDto dto,
        CancellationToken cancellationToken)
    {
        var session = await _sender.Send(
            new CreateBankReconciliationSessionCommand(
                UserId(),
                dto.BankAccountId,
                dto.PeriodStart,
                dto.PeriodEnd),
            cancellationToken);
        return Ok(ApiResponse<BankReconciliationSessionDto>.SuccessResponse(Map(session)));
    }
    [HttpGet("sessions")]
    [Authorize(Policy = "accounting.reconciliation.view")]
    public async Task<ActionResult<ApiResponse<List<BankReconciliationSessionDto>>>> Sessions(
        [FromQuery] long? bankAccountId,
        CancellationToken cancellationToken)
    {
        var sessions = await _sender.Send(
            new GetBankReconciliationSessionsQuery(bankAccountId),
            cancellationToken);
        return Ok(ApiResponse<List<BankReconciliationSessionDto>>.SuccessResponse(
            sessions.Select(Map).ToList()));
    }
    [HttpGet("sessions/{id:long}/statement-candidates")]
    [Authorize(Policy = "accounting.reconciliation.view")]
    public async Task<ActionResult<ApiResponse<List<ReconciliationCandidateDto>>>> StatementCandidates(
        long id,
        CancellationToken cancellationToken)
    {
        var candidates = await _sender.Send(
            new GetStatementCandidatesQuery(id),
            cancellationToken);
        return Ok(ApiResponse<List<ReconciliationCandidateDto>>.SuccessResponse(
            candidates.Select(Map).ToList()));
    }
    [HttpGet("sessions/{id:long}/gl-candidates")]
    [Authorize(Policy = "accounting.reconciliation.view")]
    public async Task<ActionResult<ApiResponse<List<ReconciliationCandidateDto>>>> GlCandidates(
        long id,
        CancellationToken cancellationToken)
    {
        var candidates = await _sender.Send(
            new GetGlCandidatesQuery(id),
            cancellationToken);
        return Ok(ApiResponse<List<ReconciliationCandidateDto>>.SuccessResponse(
            candidates.Select(Map).ToList()));
    }
    [HttpGet("sessions/{id:long}/calculation")]
    [Authorize(Policy = "accounting.reconciliation.view")]
    public async Task<ActionResult<ApiResponse<BankReconciliationCalculationDto>>> Calculation(
        long id,
        CancellationToken cancellationToken)
    {
        var calculation = await _sender.Send(
            new GetBankReconciliationCalculationQuery(id),
            cancellationToken);
        return Ok(ApiResponse<BankReconciliationCalculationDto>.SuccessResponse(
            Map(calculation)));
    }
    [HttpPost("matches")]
    [Authorize(Policy = "accounting.reconciliation.match")]
    public async Task<ActionResult<ApiResponse<BankReconciliationMatchDto>>> Match(
        CreateBankReconciliationMatchDto dto,
        CancellationToken cancellationToken)
    {
        var match = await _sender.Send(
            new CreateBankReconciliationMatchCommand(
                UserId(),
                dto.SessionId,
                dto.StatementLineIds,
                dto.JournalEntryLineIds),
            cancellationToken);
        return Ok(ApiResponse<BankReconciliationMatchDto>.SuccessResponse(Map(match)));
    }
    [HttpPost("matches/{id:long}/unmatch")]
    [Authorize(Policy = "accounting.reconciliation.unmatch")]
    public async Task<ActionResult<ApiResponse<BankReconciliationMatchDto>>> Unmatch(
        long id,
        UnmatchBankReconciliationDto dto,
        CancellationToken cancellationToken)
    {
        var match = await _sender.Send(
            new UnmatchBankReconciliationCommand(
                UserId(),
                id,
                dto.Reason),
            cancellationToken);
        return Ok(ApiResponse<BankReconciliationMatchDto>.SuccessResponse(Map(match)));
    }
    private static BankReconciliationSessionDto Map(BankReconciliationSessionReadModel session) => new()
    {
        Id = session.Id,
        BusinessId = session.BusinessId,
        BankAccountId = session.BankAccountId,
        PeriodStart = session.PeriodStart,
        PeriodEnd = session.PeriodEnd,
        StatementOpeningBalance = session.StatementOpeningBalance,
        StatementClosingBalance = session.StatementClosingBalance,
        GlOpeningBalance = session.GlOpeningBalance,
        Status = session.Status
    };

    private static ReconciliationCandidateDto Map(
        ReconciliationCandidateReadModel candidate) => new()
    {
        BankStatementLineId = candidate.BankStatementLineId,
        JournalEntryLineId = candidate.JournalEntryLineId,
        Date = candidate.Date,
        SignedAmount = candidate.SignedAmount,
        Description = candidate.Description,
        ExternalReference = candidate.ExternalReference,
        JournalEntryId = candidate.JournalEntryId,
        JournalNumber = candidate.JournalNumber,
        SourceType = candidate.SourceType,
        SourceReference = candidate.SourceReference
    };

    private static BankReconciliationCalculationDto Map(
        BankReconciliationCalculationReadModel calculation) => new()
    {
        StatementOpeningBalance = calculation.StatementOpeningBalance,
        StatementMovement = calculation.StatementMovement,
        StatementClosingBalance = calculation.StatementClosingBalance,
        GlOpeningBalance = calculation.GlOpeningBalance,
        GlMovement = calculation.GlMovement,
        GlClosingBalance = calculation.GlClosingBalance,
        MatchedStatementAmount = calculation.MatchedStatementAmount,
        MatchedStatementCount = calculation.MatchedStatementCount,
        MatchedGlAmount = calculation.MatchedGlAmount,
        MatchedGlCount = calculation.MatchedGlCount,
        UnmatchedStatementAmount = calculation.UnmatchedStatementAmount,
        UnmatchedStatementCount = calculation.UnmatchedStatementCount,
        UnmatchedGlAmount = calculation.UnmatchedGlAmount,
        UnmatchedGlCount = calculation.UnmatchedGlCount,
        ReconciliationDifference = calculation.ReconciliationDifference,
        UnresolvedExceptionCount = calculation.UnresolvedExceptionCount
    };

    private static BankReconciliationMatchDto Map(
        ReconciliationMatchReadModel match) => new()
    {
        Id = match.Id,
        SessionId = match.SessionId,
        Status = match.Status,
        StatementTotal = match.StatementTotal,
        GlTotal = match.GlTotal
    };

    private long UserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException("User identity not found.");
}
