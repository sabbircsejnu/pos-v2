using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/final-settlements")]
public sealed class HrFinalSettlementController(
    ISender sender,
    ILogger<HrFinalSettlementController> logger) : ControllerBase
{
    [HttpGet("{id:long}")]
    [Authorize(Policy = "payroll.final_settlement.view")]
    [ProducesResponseType<PayrollFinalSettlementModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PayrollFinalSettlementModel>> Get(
        long id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await sender.Send(new GetPayrollFinalSettlementQuery(id), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Final settlement access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Final settlement access denied.", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid final settlement request.", detail: exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Final settlement reference not found.", detail: exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("Final settlement access conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Final settlement access conflict.", detail: exception.Message);
        }
    }

    [HttpGet("{id:long}/execution")]
    [Authorize(Policy = "payroll.final_settlement.financial.view")]
    [ProducesResponseType<PayrollFinalSettlementExecutionStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ActionResult<PayrollFinalSettlementExecutionStatus>> GetExecution(
        long id, CancellationToken cancellationToken) =>
        ExecutePayment<PayrollFinalSettlementExecutionStatus>(async () =>
            Ok(await sender.Send(
                new GetPayrollFinalSettlementExecutionStatusQuery(id), cancellationToken)));

    [HttpPost("{id:long}/payments")]
    [Authorize(Policy = "payroll.final_settlement.payment.create")]
    [ProducesResponseType<PayrollPaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<PayrollPaymentResponse>> CreatePayment(
        long id,
        [FromBody] CreateFinalSettlementPaymentRequest request,
        CancellationToken cancellationToken) =>
        ExecutePayment<PayrollPaymentResponse>(async () =>
            Ok(await sender.Send(
                new CreateFinalSettlementPaymentCommand(id, request), cancellationToken)));

    [HttpPost("{id:long}/accounting")]
    [Authorize(Policy = "payroll.final_settlement.accounting.post")]
    [ProducesResponseType<PayrollFinalSettlementAccountingStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<ActionResult<PayrollFinalSettlementAccountingStatus>> PostAccounting(
        long id,
        [FromBody] FinalSettlementAccountingPostRequest request,
        CancellationToken cancellationToken) =>
        ExecutePayment<PayrollFinalSettlementAccountingStatus>(async () =>
            Ok(await sender.Send(new PostPayrollFinalSettlementAccountingCommand(
                id, request.RevisionNo, request.IdempotencyKey), cancellationToken)));

    [HttpPost("{id:long}/submit")]
    [Authorize(Policy = "payroll.final_settlement.submit")]
    [ProducesResponseType<ApprovalDecisionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ApprovalDecisionResult>> Submit(
        long id, [FromBody] FinalSettlementSubmitRequest request, CancellationToken cancellationToken) =>
        ExecuteApproval<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new SubmitPayrollFinalSettlementCommand(id, request.RevisionNo, request.IdempotencyKey),
            cancellationToken)));

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = "payroll.final_settlement.approve")]
    [ProducesResponseType<ApprovalDecisionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ApprovalDecisionResult>> Approve(
        long id, [FromBody] FinalSettlementDecisionRequest request, CancellationToken cancellationToken) =>
        ExecuteApproval<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new ApprovePayrollFinalSettlementCommand(
                id, request.RevisionNo, request.DecisionKey, request.Reason), cancellationToken)));

    [HttpPost("{id:long}/reject")]
    [Authorize(Policy = "payroll.final_settlement.reject")]
    [ProducesResponseType<ApprovalDecisionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ApprovalDecisionResult>> Reject(
        long id, [FromBody] FinalSettlementDecisionRequest request, CancellationToken cancellationToken) =>
        ExecuteApproval<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new RejectPayrollFinalSettlementCommand(
                id, request.RevisionNo, request.DecisionKey, request.Reason ?? ""), cancellationToken)));

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = "payroll.final_settlement.cancel")]
    [ProducesResponseType<ApprovalDecisionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<ApprovalDecisionResult>> Cancel(
        long id, [FromBody] FinalSettlementDecisionRequest request, CancellationToken cancellationToken) =>
        ExecuteApproval<ApprovalDecisionResult>(async () => Ok(await sender.Send(
            new CancelPayrollFinalSettlementApprovalCommand(
                id, request.RevisionNo, request.DecisionKey, request.Reason ?? ""), cancellationToken)));

    private async Task<ActionResult<T>> ExecuteApproval<T>(Func<Task<ActionResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Final settlement approval access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Final settlement approval access denied.", detail: exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "Final settlement approval reference not found.", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid final settlement approval request.", detail: exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("Final settlement approval conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Final settlement approval conflict.", detail: exception.Message);
        }
    }

    private async Task<ActionResult<T>> ExecutePayment<T>(Func<Task<ActionResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Final settlement execution access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Final settlement execution access denied.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid final settlement execution request.", detail: exception.Message);
        }
        catch (PayrollPaymentConflictException exception)
        {
            logger.LogWarning("Final settlement execution conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Final settlement execution conflict.", detail: exception.Message);
        }
        catch (PayrollFinalSettlementAccountingConflictException exception)
        {
            logger.LogWarning("Final settlement Accounting conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Final settlement Accounting conflict.", detail: exception.Message);
        }
        catch (PayrollFinalSettlementAccountingException exception)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Final settlement Accounting request could not be validated.",
                detail: exception.Message);
        }
        catch (FrozenJournalConflictException exception)
        {
            logger.LogError(exception, "Final settlement Accounting identity conflict.");
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Final settlement Accounting conflict.");
        }
    }
}

/// <summary>Submits a frozen final settlement revision for approval.</summary>
public sealed record FinalSettlementSubmitRequest(int RevisionNo, string IdempotencyKey);

/// <summary>Records an approval decision for a frozen final settlement revision.</summary>
public sealed record FinalSettlementDecisionRequest(int RevisionNo, string DecisionKey, string? Reason);

/// <summary>Posts the currently approved final settlement revision idempotently.</summary>
public sealed record FinalSettlementAccountingPostRequest(int RevisionNo, string IdempotencyKey);
