using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/periods/{periodId:long}/runs")]
public sealed class PayrollRunsController(
    ISender sender,
    ILogger<PayrollRunsController> logger) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "payroll.run.create")]
    public Task<ActionResult<PayrollRunResponse>> Create(
        long periodId, CreatePayrollRunRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollRunResponse>(async () =>
        {
            var run = await sender.Send(new CreatePayrollRunCommand(periodId, request), cancellationToken);
            return CreatedAtAction(nameof(Get), new { periodId, runId = run.Id }, run);
        });

    [HttpPost("{originalRunId:long}/corrections")]
    [Authorize(Policy = "payroll.run.reopen")]
    public Task<ActionResult<PayrollRunResponse>> CreateCorrection(
        long periodId,
        long originalRunId,
        CreatePayrollCorrectionRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollRunResponse>(async () =>
        {
            var run = await sender.Send(
                new CreatePayrollCorrectionCommand(periodId, originalRunId, request), cancellationToken);
            return CreatedAtAction(nameof(Get), new { periodId, runId = run.Id }, run);
        });

    [HttpPost("off-cycle")]
    [Authorize(Policy = "payroll.run.create")]
    public Task<ActionResult<PayrollRunResponse>> CreateOffCycle(
        long periodId,
        CreateOffCyclePayrollRunRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollRunResponse>(async () =>
        {
            var run = await sender.Send(
                new CreateOffCyclePayrollRunCommand(periodId, request), cancellationToken);
            return CreatedAtAction(nameof(Get), new { periodId, runId = run.Id }, run);
        });

    [HttpGet("{runId:long}")]
    [Authorize(Policy = "payroll.run.view")]
    public Task<ActionResult<PayrollRunResponse>> Get(
        long periodId, long runId, CancellationToken cancellationToken) =>
        Execute<PayrollRunResponse>(async () =>
            Ok(await sender.Send(new GetPayrollRunQuery(periodId, runId), cancellationToken)));

    [HttpGet("{runId:long}/correction")]
    [Authorize(Policy = "payroll.run.view")]
    public Task<ActionResult<PayrollRunCorrectionResponse>> GetCorrection(
        long periodId, long runId, CancellationToken cancellationToken) =>
        Execute<PayrollRunCorrectionResponse>(async () =>
            Ok(await sender.Send(
                new GetPayrollRunCorrectionQuery(periodId, runId), cancellationToken)));

    [HttpGet("{runId:long}/eligibility")]
    [Authorize(Policy = "payroll.run.view")]
    public Task<ActionResult<PayrollRunEligibilityPageResponse>> GetEligibility(
        long periodId,
        long runId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Execute<PayrollRunEligibilityPageResponse>(async () =>
            Ok(await sender.Send(
                new GetPayrollRunEligibilityQuery(periodId, runId, page, pageSize), cancellationToken)));

    [HttpGet("{runId:long}/validation")]
    [Authorize(Policy = "payroll.run.view")]
    public Task<ActionResult<PayrollRunValidationResponse>> GetValidation(
        long periodId,
        long runId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Execute<PayrollRunValidationResponse>(async () =>
            Ok(await sender.Send(
                new GetPayrollRunValidationQuery(periodId, runId, page, pageSize), cancellationToken)));

    [HttpPost("{runId:long}/calculate")]
    [Authorize(Policy = "payroll.run.calculate")]
    public Task<ActionResult<PayrollRunCalculationResponse>> Calculate(
        long periodId, long runId, CancellationToken cancellationToken) =>
        Execute<PayrollRunCalculationResponse>(async () =>
            Ok(await sender.Send(new CalculatePayrollRunCommand(periodId, runId), cancellationToken)));

    [HttpPost("{runId:long}/submit")]
    [Authorize(Policy = "payroll.run.submit")]
    public Task<ActionResult<ApprovalDecisionResult>> Submit(
        long periodId, long runId, SubmitPayrollRunRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new SubmitPayrollRunCommand(
                periodId, runId, request.ExpectedVersion), cancellationToken)));

    [HttpPost("~/api/payroll/v1/approvals/runs/{instanceId:long}/approve")]
    [Authorize(Policy = "payroll.run.approve")]
    public Task<ActionResult<ApprovalDecisionResult>> Approve(
        long instanceId, PayrollRunApprovalDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new RecordApprovalDecisionCommand(
                instanceId, request.DecisionKey, ApprovalDecisionType.Approve, request.Reason),
                cancellationToken)));

    [HttpPost("~/api/payroll/v1/approvals/runs/{instanceId:long}/reject")]
    [Authorize(Policy = "payroll.run.approve")]
    public Task<ActionResult<ApprovalDecisionResult>> Reject(
        long instanceId, PayrollRunApprovalDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new RecordApprovalDecisionCommand(
                instanceId, request.DecisionKey, ApprovalDecisionType.Reject, request.Reason),
                cancellationToken)));

    [HttpPost("~/api/payroll/v1/approvals/runs/{instanceId:long}/cancel")]
    [Authorize(Policy = "payroll.run.submit")]
    public Task<ActionResult<ApprovalDecisionResult>> Cancel(
        long instanceId, PayrollRunApprovalDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new CancelApprovalCommand(
                instanceId, request.DecisionKey, request.Reason ?? string.Empty), cancellationToken)));

    [HttpGet("{runId:long}/results")]
    [Authorize(Policy = "payroll.run.view")]
    public Task<ActionResult<PayrollEmployeeResultPageResponse>> GetResults(
        long periodId,
        long runId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Execute<PayrollEmployeeResultPageResponse>(async () =>
            Ok(await sender.Send(
                new GetPayrollEmployeeResultsQuery(periodId, runId, page, pageSize), cancellationToken)));

    [HttpGet("{runId:long}/results/{employeeId:long}")]
    [Authorize(Policy = "payroll.run.view")]
    public Task<ActionResult<PayrollEmployeeResultDetailsResponse>> GetResult(
        long periodId, long runId, long employeeId, CancellationToken cancellationToken) =>
        Execute<PayrollEmployeeResultDetailsResponse>(async () =>
            Ok(await sender.Send(
                new GetPayrollEmployeeResultQuery(periodId, runId, employeeId), cancellationToken)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll run access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Payroll run access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll run request.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ApprovalKernelConflictException exception)
        {
            logger.LogWarning("Payroll run approval conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll run approval conflict.");
        }
        catch (ApprovalKernelUnavailableException exception)
        {
            logger.LogWarning("Payroll run approval unavailable: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll run approval is not configured.", detail: exception.Message);
        }
        catch (PayrollRunConflictException exception)
        {
            logger.LogWarning("Payroll run conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll run conflict.", detail: exception.Message);
        }
    }
}
