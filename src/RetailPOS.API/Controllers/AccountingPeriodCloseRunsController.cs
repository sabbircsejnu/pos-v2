using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.AccountingCloseRuns.Commands;
using RetailPOS.Application.AccountingCloseRuns.Queries;
using RetailPOS.Application.AccountingCloseRuns.ReadModels;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/period-close-runs")]
[Authorize]
public sealed class AccountingPeriodCloseRunsController : ControllerBase
{
    private readonly ISender _sender;
    public AccountingPeriodCloseRunsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet][Authorize(Policy = "accounting.close.audit.view")]
    public async Task<ActionResult<ApiResponse<List<AccountingPeriodCloseRunDto>>>> List([FromQuery] long? accountingPeriodId, CancellationToken cancellationToken)
    {
        var runs = await _sender.Send(
            new ListAccountingPeriodCloseRunsQuery(accountingPeriodId),
            cancellationToken);
        return Ok(ApiResponse<List<AccountingPeriodCloseRunDto>>.SuccessResponse(
            runs.Select(Map).ToList()));
    }
    [HttpGet("{id:long}")][Authorize(Policy = "accounting.close.audit.view")]
    public async Task<ActionResult<ApiResponse<AccountingPeriodCloseRunDto>>> Get(long id, CancellationToken cancellationToken)
    {
        var run = await _sender.Send(
            new GetAccountingPeriodCloseRunQuery(id),
            cancellationToken);
        return Ok(ApiResponse<AccountingPeriodCloseRunDto>.SuccessResponse(Map(run)));
    }
    [HttpGet("{id:long}/package")][Authorize(Policy = "accounting.close.audit.view")]
    public async Task<IActionResult> Package(long id, CancellationToken cancellationToken)
    {
        var run = await _sender.Send(
            new GetAccountingPeriodCloseRunQuery(id),
            cancellationToken);
        return File(
            JsonSerializer.SerializeToUtf8Bytes(Map(run), new JsonSerializerOptions { WriteIndented = true }),
            "application/json",
            $"period-close-run-{id}.json");
    }
    [HttpPost("periods/{accountingPeriodId:long}/prepare")][Authorize(Policy = "accounting.close.prepare")]
    public async Task<ActionResult<ApiResponse<AccountingPeriodCloseRunDto>>> Prepare(
        long accountingPeriodId,
        CancellationToken cancellationToken)
    {
        var runId = await _sender.Send(
            new PrepareAccountingPeriodCloseRunCommand(accountingPeriodId, UserId()),
            cancellationToken);
        var run = await _sender.Send(
            new GetAccountingPeriodCloseRunQuery(runId),
            cancellationToken);
        return Ok(ApiResponse<AccountingPeriodCloseRunDto>.SuccessResponse(Map(run)));
    }
    [HttpPost("{id:long}/re-evaluate")][Authorize(Policy = "accounting.close.prepare")]
    public async Task<ActionResult<ApiResponse<AccountingPeriodCloseRunDto>>> Reevaluate(
        long id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ReevaluateAccountingPeriodCloseRunCommand(id, UserId()),
            cancellationToken);
        var run = await _sender.Send(
            new GetAccountingPeriodCloseRunQuery(id),
            cancellationToken);
        return Ok(ApiResponse<AccountingPeriodCloseRunDto>.SuccessResponse(Map(run)));
    }
    [HttpPost("{id:long}/gates/{gateSnapshotId:long}/approve-exception")][Authorize(Policy = "accounting.close.override_exception")]
    public async Task<ActionResult<ApiResponse<AccountingPeriodCloseRunDto>>> ApproveException(
        long id,
        long gateSnapshotId,
        ApproveCloseExceptionDto dto,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ApproveAccountingPeriodCloseExceptionCommand(
                id,
                gateSnapshotId,
                dto.Reason,
                UserId()),
            cancellationToken);
        var run = await _sender.Send(
            new GetAccountingPeriodCloseRunQuery(id),
            cancellationToken);
        return Ok(ApiResponse<AccountingPeriodCloseRunDto>.SuccessResponse(Map(run)));
    }
    [HttpPost("{id:long}/approve")][Authorize(Policy = "accounting.close.approve")]
    public async Task<ActionResult<ApiResponse<AccountingPeriodCloseRunDto>>> Approve(
        long id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ApproveAccountingPeriodCloseRunCommand(id, UserId()),
            cancellationToken);
        var run = await _sender.Send(
            new GetAccountingPeriodCloseRunQuery(id),
            cancellationToken);
        return Ok(ApiResponse<AccountingPeriodCloseRunDto>.SuccessResponse(Map(run)));
    }
    [HttpPost("{id:long}/execute")][Authorize(Policy = "accounting.close.execute")]
    public async Task<ActionResult<ApiResponse<AccountingPeriodCloseRunDto>>> Execute(
        long id,
        CloseRunReasonDto? dto,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ExecuteAccountingPeriodCloseRunCommand(
                id,
                dto?.Reason,
                UserId()),
            cancellationToken);
        var run = await _sender.Send(
            new GetAccountingPeriodCloseRunQuery(id),
            cancellationToken);
        return Ok(ApiResponse<AccountingPeriodCloseRunDto>.SuccessResponse(Map(run)));
    }
    [HttpPost("{id:long}/reopen")][Authorize(Policy = "accounting.periods.reopen")]
    public async Task<ActionResult<ApiResponse<AccountingPeriodCloseRunDto>>> Reopen(
        long id,
        RequiredReasonActionDto dto,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ReopenAccountingPeriodCloseRunCommand(
                id,
                dto.Reason,
                UserId()),
            cancellationToken);
        var run = await _sender.Send(
            new GetAccountingPeriodCloseRunQuery(id),
            cancellationToken);
        return Ok(ApiResponse<AccountingPeriodCloseRunDto>.SuccessResponse(Map(run)));
    }

    private static AccountingPeriodCloseRunDto Map(AccountingPeriodCloseRunReadModel model) => new()
    {
        Id = model.Id,
        AccountingPeriodId = model.AccountingPeriodId,
        RunNumber = model.RunNumber,
        Status = model.Status,
        EvaluationSequence = model.EvaluationSequence,
        PreparedBy = model.PreparedBy,
        PreparedAt = model.PreparedAt,
        ApprovedBy = model.ApprovedBy,
        ApprovedAt = model.ApprovedAt,
        ExecutedBy = model.ExecutedBy,
        ExecutedAt = model.ExecutedAt,
        ExecutionReason = model.ExecutionReason,
        ReopenedBy = model.ReopenedBy,
        ReopenedAt = model.ReopenedAt,
        ReopenReason = model.ReopenReason,
        Gates = model.Gates.Select(gate => new AccountingCloseGateSnapshotDto
        {
            Id = gate.Id,
            EvaluationSequence = gate.EvaluationSequence,
            GateCode = gate.GateCode,
            Status = gate.Status,
            ExpectedAmount = gate.ExpectedAmount,
            ActualAmount = gate.ActualAmount,
            Details = gate.Details,
            IsHistoricalCutoverLimitation = gate.IsHistoricalCutoverLimitation,
            EvaluatedAt = gate.EvaluatedAt,
            HasApprovedException = gate.HasApprovedException,
            ApprovedExceptionReason = gate.ApprovedExceptionReason,
            ExceptionApprovedBy = gate.ExceptionApprovedBy,
            ExceptionApprovedAt = gate.ExceptionApprovedAt
        }).ToList()
    };

    private long UserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : throw new UnauthorizedAccessException("User identity not found.");
}