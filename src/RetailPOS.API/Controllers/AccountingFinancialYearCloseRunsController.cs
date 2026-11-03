using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.FinancialYearCloseRuns.Commands;
using RetailPOS.Application.FinancialYearCloseRuns.Queries;
using RetailPOS.Application.FinancialYearCloseRuns.ReadModels;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/financial-year-close-runs")]
[Authorize]
public sealed class AccountingFinancialYearCloseRunsController : ControllerBase
{
    private readonly ISender _sender;
    public AccountingFinancialYearCloseRunsController(ISender sender)
    {
        _sender = sender;
    }
    [HttpGet][Authorize(Policy = "accounting.close.audit.view")]
    public async Task<ActionResult<ApiResponse<List<AccountingFinancialYearCloseRunDto>>>> List(
        [FromQuery] long? financialYearId,
        CancellationToken cancellationToken)
    {
        var runs = await _sender.Send(
            new ListAccountingFinancialYearCloseRunsQuery(financialYearId),
            cancellationToken);
        return Ok(ApiResponse<List<AccountingFinancialYearCloseRunDto>>.SuccessResponse(
            runs.Select(Map).ToList()));
    }
    [HttpGet("{id:long}")][Authorize(Policy = "accounting.close.audit.view")]
    public async Task<ActionResult<ApiResponse<AccountingFinancialYearCloseRunDto>>> Get(
        long id,
        CancellationToken cancellationToken)
    {
        var run = await _sender.Send(
            new GetAccountingFinancialYearCloseRunQuery(id),
            cancellationToken);
        return Ok(ApiResponse<AccountingFinancialYearCloseRunDto>.SuccessResponse(Map(run)));
    }
    [HttpGet("{id:long}/package")][Authorize(Policy = "accounting.close.audit.view")]
    public async Task<IActionResult> Package(long id, CancellationToken cancellationToken)
    {
        var run = await _sender.Send(
            new GetAccountingFinancialYearCloseRunQuery(id),
            cancellationToken);
        return File(
            JsonSerializer.SerializeToUtf8Bytes(Map(run), new JsonSerializerOptions { WriteIndented = true }),
            "application/json",
            $"financial-year-close-run-{id}.json");
    }
    [HttpPost("financial-years/{financialYearId:long}/prepare")][Authorize(Policy = "accounting.close.prepare")]
    public async Task<ActionResult<ApiResponse<AccountingFinancialYearCloseRunDto>>> Prepare(
        long financialYearId,
        CancellationToken cancellationToken)
    {
        var runId = await _sender.Send(
            new PrepareAccountingFinancialYearCloseRunCommand(financialYearId, UserId()),
            cancellationToken);
        var run = await _sender.Send(
            new GetAccountingFinancialYearCloseRunQuery(runId),
            cancellationToken);
        return Ok(ApiResponse<AccountingFinancialYearCloseRunDto>.SuccessResponse(Map(run)));
    }
    [HttpPost("{id:long}/approve")][Authorize(Policy = "accounting.close.approve")]
    public async Task<ActionResult<ApiResponse<AccountingFinancialYearCloseRunDto>>> Approve(
        long id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ApproveAccountingFinancialYearCloseRunCommand(id, UserId()),
            cancellationToken);
        var run = await _sender.Send(
            new GetAccountingFinancialYearCloseRunQuery(id),
            cancellationToken);
        return Ok(ApiResponse<AccountingFinancialYearCloseRunDto>.SuccessResponse(Map(run)));
    }
    [HttpPost("{id:long}/execute")][Authorize(Policy = "accounting.close.execute")]
    public async Task<ActionResult<ApiResponse<AccountingFinancialYearCloseRunDto>>> Execute(
        long id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ExecuteAccountingFinancialYearCloseRunCommand(id, UserId()),
            cancellationToken);
        var run = await _sender.Send(
            new GetAccountingFinancialYearCloseRunQuery(id),
            cancellationToken);
        return Ok(ApiResponse<AccountingFinancialYearCloseRunDto>.SuccessResponse(Map(run)));
    }
    [HttpPost("{id:long}/reopen")][Authorize(Policy = "accounting.financial_years.reopen")]
    public async Task<ActionResult<ApiResponse<AccountingFinancialYearCloseRunDto>>> Reopen(
        long id,
        ReopenFinancialYearCloseDto dto,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ReopenAccountingFinancialYearCloseRunCommand(
                id,
                dto.Reason,
                dto.PostingDate,
                UserId()),
            cancellationToken);
        var run = await _sender.Send(
            new GetAccountingFinancialYearCloseRunQuery(id),
            cancellationToken);
        return Ok(ApiResponse<AccountingFinancialYearCloseRunDto>.SuccessResponse(Map(run)));
    }

    private static AccountingFinancialYearCloseRunDto Map(
        AccountingFinancialYearCloseRunReadModel model) => new()
    {
        Id = model.Id,
        FinancialYearId = model.FinancialYearId,
        FinalAccountingPeriodId = model.FinalAccountingPeriodId,
        FinalPeriodCloseRunId = model.FinalPeriodCloseRunId,
        RunNumber = model.RunNumber,
        Status = model.Status,
        PreparedBy = model.PreparedBy,
        PreparedAt = model.PreparedAt,
        ApprovedBy = model.ApprovedBy,
        ApprovedAt = model.ApprovedAt,
        ExecutedBy = model.ExecutedBy,
        ExecutedAt = model.ExecutedAt,
        YearEndJournalEntryId = model.YearEndJournalEntryId,
        YearEndJournalNumber = model.YearEndJournalNumber,
        ReversalJournalEntryId = model.ReversalJournalEntryId,
        ReopenedBy = model.ReopenedBy,
        ReopenedAt = model.ReopenedAt,
        ReopenReason = model.ReopenReason
    };

    private long UserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : throw new UnauthorizedAccessException("User identity not found.");
}