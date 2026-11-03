using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/exits")]
public sealed class HrExitController(ISender sender, ILogger<HrExitController> logger) : ControllerBase
{
    [HttpGet("templates")]
    [Authorize(Policy = "hr.exit.view")]
    public Task<ActionResult<IReadOnlyList<HrClearanceTemplateModel>>> GetTemplates(CancellationToken ct) =>
        Execute<IReadOnlyList<HrClearanceTemplateModel>>(async () =>
            Ok(await sender.Send(new GetHrClearanceTemplatesQuery(), ct)));

    [HttpPost("templates")]
    [Authorize(Policy = "hr.exit.clearance")]
    public Task<ActionResult<HrClearanceTemplateModel>> CreateTemplate(
        [FromBody] HrClearanceTemplateRequest request, CancellationToken ct) =>
        Execute<HrClearanceTemplateModel>(async () =>
        {
            var input = new HrClearanceTemplateInput(request.Code, request.Name, request.ScopeType,
                request.ScopeReference, request.VersionNo, request.EffectiveFrom, request.EffectiveTo,
                request.IsActive, request.Items);
            var created = await sender.Send(new CreateHrClearanceTemplateCommand(input), ct);
            return CreatedAtAction(nameof(GetTemplates), null, created);
        });

    [HttpGet]
    [Authorize(Policy = "hr.exit.view")]
    public Task<ActionResult<IReadOnlyList<HrExitCaseModel>>> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? status,
        [FromQuery] long? employeeId, CancellationToken ct) =>
        Execute<IReadOnlyList<HrExitCaseModel>>(async () => Ok(await sender.Send(
            new GetHrExitCasesQuery(from, to, status, employeeId), ct)));

    [HttpGet("{id:long}", Name = nameof(Get))]
    [Authorize(Policy = "hr.exit.view")]
    public Task<ActionResult<HrExitCaseModel>> Get(long id, CancellationToken ct) =>
        Execute<HrExitCaseModel>(async () =>
        {
            var result = await sender.Send(new GetHrExitCaseQuery(id), ct);
            return result is null ? NotFound() : Ok(result);
        });

    [HttpPost]
    [Authorize(Policy = "hr.exit.initiate")]
    public Task<ActionResult<HrExitCaseModel>> Initiate(
        [FromBody] HrExitCaseRequest request, CancellationToken ct) =>
        Execute<HrExitCaseModel>(async () =>
        {
            var created = await sender.Send(new InitiateHrExitCommand(new HrExitCaseInput(
                request.EmployeeId, request.ExitType, request.InitiatedDate,
                request.ProposedLastWorkingDate, request.Reason)), ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        });

    [HttpPost("{id:long}/submit")]
    [Authorize(Policy = "hr.exit.initiate")]
    public Task<ActionResult<ApprovalDecisionResult>> Submit(
        long id, [FromBody] HrExitVersionRequest request, CancellationToken ct) =>
        Execute<ApprovalDecisionResult>(async () => AcceptedAtAction(nameof(Get), new { id },
            await sender.Send(new SubmitHrExitCommand(id, request.ExpectedVersion), ct)));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = "hr.exit.initiate")]
    public Task<ActionResult<ApprovalDecisionResult>> Cancel(
        long id, [FromBody] HrExitCancelRequest request, CancellationToken ct) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(new CancelHrExitCommand(
            id, request.ExpectedVersion, request.DecisionKey, request.Reason), ct)));

    [HttpPut("clearance-items")]
    [Authorize(Policy = "hr.exit.clearance")]
    public Task<ActionResult<HrExitCaseModel>> UpdateClearance(
        [FromBody] HrClearanceUpdateInput request, CancellationToken ct) =>
        Execute<HrExitCaseModel>(async () =>
        {
            var result = await sender.Send(new UpdateHrClearanceItemCommand(request), ct);
            return result is null ? NotFound() : Ok(result);
        });

    [HttpPost("{id:long}/complete")]
    [Authorize(Policy = "hr.exit.clearance")]
    public Task<ActionResult<HrExitCaseModel>> Complete(
        long id, [FromBody] HrExitCompleteRequest request, CancellationToken ct) =>
        Execute<HrExitCaseModel>(async () =>
        {
            var result = await sender.Send(new CompleteNonFinancialHrExitCommand(
                id, request.ExpectedVersion, request.ActualLastWorkingDate, request.Reason), ct);
            return result is null ? NotFound() : Ok(result);
        });

    [HttpGet("{id:long}/completion-preview")]
    [Authorize(Policy = "hr.exit.clearance")]
    [ProducesResponseType<HrExitCompletionPreviewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ActionResult<HrExitCompletionPreviewModel>> GetCompletionPreview(
        long id, CancellationToken ct) =>
        Execute<HrExitCompletionPreviewModel>(async () =>
        {
            var result = await sender.Send(new GetHrExitCompletionPreviewQuery(id), ct);
            return result is null ? NotFound() : Ok(result);
        });

    [HttpPost("{id:long}/final-settlement/calculate")]
    [Authorize(Policy = "payroll.final_settlement.calculate")]
    [ProducesResponseType<PayrollFinalSettlementModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<PayrollFinalSettlementModel>> CalculateFinalSettlement(
        long id, [FromBody] HrFinalSettlementCalculateRequest request, CancellationToken ct) =>
        Execute<PayrollFinalSettlementModel>(async () => Ok(await sender.Send(
            new CalculatePayrollFinalSettlementCommand(
                id, request.ExpectedExitVersion, request.IdempotencyKey), ct)));

    [HttpGet("{id:long}/final-settlement")]
    [Authorize(Policy = "payroll.final_settlement.view")]
    [ProducesResponseType<PayrollFinalSettlementModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ActionResult<PayrollFinalSettlementModel>> GetFinalSettlementForExit(
        long id, CancellationToken ct) =>
        Execute<PayrollFinalSettlementModel>(async () =>
        {
            var result = await sender.Send(new GetPayrollFinalSettlementForExitQuery(id), ct);
            return result is null ? NotFound() : Ok(result);
        });

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Exit access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Exit access denied.", detail: exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Exit reference not found.", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid exit request.", detail: exception.Message);
        }
        catch (HrExitConcurrencyException exception)
        {
            logger.LogWarning(exception, "Exit operation encountered stale state.");
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Exit operation conflict.", detail: exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("Exit operation conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Exit operation conflict.", detail: exception.Message);
        }
    }
}

public sealed record HrClearanceTemplateRequest(
    string Code, string Name, RetailPOS.Core.Entities.HR.Exit.HrClearanceTemplateScopeType ScopeType,
    string? ScopeReference, int VersionNo, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    bool IsActive, IReadOnlyList<HrClearanceTemplateItemInput> Items);
public sealed record HrExitCaseRequest(
    long EmployeeId, string ExitType, DateOnly InitiatedDate,
    DateOnly ProposedLastWorkingDate, string Reason);
public sealed record HrExitVersionRequest(long ExpectedVersion);
public sealed record HrExitCancelRequest(long ExpectedVersion, string DecisionKey, string Reason);
public sealed record HrExitCompleteRequest(long ExpectedVersion, DateOnly ActualLastWorkingDate, string Reason);
/// <summary>Identifies the expected approved exit version and idempotent calculation request.</summary>
public sealed record HrFinalSettlementCalculateRequest(long ExpectedExitVersion, string IdempotencyKey);
