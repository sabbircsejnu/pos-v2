using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/loans")]
public sealed class PayrollEmployeeLoanController(
    ISender sender,
    ILogger<PayrollEmployeeLoanController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "payroll.loan.view")]
    public Task<ActionResult<IReadOnlyList<PayrollEmployeeLoanResponse>>> EmployeeLoans(
        [FromQuery] long employeeId, CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollEmployeeLoanResponse>>(async () =>
        {
            if (employeeId <= 0)
                throw new ArgumentException("An employee ID is required to list scoped loan records.");
            return Ok(await sender.Send(new GetEmployeePayrollLoansQuery(employeeId), cancellationToken));
        });

    [HttpPost]
    [Authorize(Policy = "payroll.loan.manage")]
    public Task<ActionResult<PayrollEmployeeLoanResponse>> Create(
        CreatePayrollEmployeeLoanRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollEmployeeLoanResponse>(async () =>
        {
            var result = await sender.Send(new CreatePayrollEmployeeLoanCommand(request), cancellationToken);
            return result.IsReplay ? Ok(result) : CreatedAtAction(
                nameof(Loan), new { id = result.Id }, result);
        });

    [HttpGet("{id:long}")]
    [Authorize(Policy = "payroll.loan.view")]
    public Task<ActionResult<PayrollEmployeeLoanResponse>> Loan(
        long id, CancellationToken cancellationToken) =>
        Execute<PayrollEmployeeLoanResponse>(async () => Ok(await sender.Send(
            new GetPayrollEmployeeLoanQuery(id), cancellationToken)));

    [HttpPost("{id:long}/submit")]
    [Authorize(Policy = "payroll.loan.manage")]
    public Task<ActionResult<ApprovalDecisionResult>> Submit(
        long id, PayrollLoanVersionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new SubmitPayrollEmployeeLoanCommand(id, request.ExpectedVersion), cancellationToken)));

    [HttpPost("{id:long}/repayments")]
    [Authorize(Policy = "payroll.loan.manage")]
    public Task<ActionResult<PayrollEmployeeLoanResponse>> RecordRepayment(
        long id, RecordPayrollLoanRepaymentRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollEmployeeLoanResponse>(async () => Ok(await sender.Send(
            new RecordPayrollLoanRepaymentCommand(id, request), cancellationToken)));

    [HttpPut("{id:long}/schedule")]
    [Authorize(Policy = "payroll.loan.manage")]
    public Task<ActionResult<PayrollEmployeeLoanResponse>> UpdateSchedule(
        long id, UpdatePayrollLoanScheduleRequest request, CancellationToken cancellationToken) =>
        Execute<PayrollEmployeeLoanResponse>(async () => Ok(await sender.Send(
            new UpdatePayrollLoanScheduleCommand(id, request), cancellationToken)));

    [HttpPost("approvals/{instanceId:long}/approve")]
    [Authorize(Policy = "payroll.loan.approve")]
    public Task<ActionResult<ApprovalDecisionResult>> Approve(
        long instanceId, PayrollLoanDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new RecordApprovalDecisionCommand(instanceId, request.DecisionKey,
                RetailPOS.Core.Entities.HR.Approvals.ApprovalDecisionType.Approve, request.Reason),
            cancellationToken)));

    [HttpPost("approvals/{instanceId:long}/reject")]
    [Authorize(Policy = "payroll.loan.approve")]
    public Task<ActionResult<ApprovalDecisionResult>> Reject(
        long instanceId, PayrollLoanDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new RecordApprovalDecisionCommand(instanceId, request.DecisionKey,
                RetailPOS.Core.Entities.HR.Approvals.ApprovalDecisionType.Reject, request.Reason),
            cancellationToken)));

    [HttpPost("approvals/{instanceId:long}/cancel")]
    [Authorize(Policy = "payroll.loan.manage")]
    public Task<ActionResult<ApprovalDecisionResult>> Cancel(
        long instanceId, PayrollLoanDecisionRequest request, CancellationToken cancellationToken) =>
        Execute<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new CancelApprovalCommand(instanceId, request.DecisionKey, request.Reason ?? ""),
            cancellationToken)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll loan access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Payroll access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid loan request.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PayrollSalaryConflictException exception)
        {
            logger.LogWarning("Payroll loan conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Loan conflict.", detail: exception.Message);
        }
        catch (ApprovalKernelConflictException exception)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Approval conflict.", detail: exception.Message);
        }
        catch (ApprovalKernelUnavailableException exception)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Approval is unavailable.", detail: exception.Message);
        }
    }
}

public sealed record PayrollLoanVersionRequest(long ExpectedVersion);
public sealed record PayrollLoanDecisionRequest(string DecisionKey, string? Reason);
