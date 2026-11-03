using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1")]
public sealed class PayrollAccountingController(
    ISender sender,
    ILogger<PayrollAccountingController> logger) : ControllerBase
{
    [HttpPost("runs/{runId:long}/post-accounting")]
    [Authorize(Policy = "payroll.accounting.post")]
    public Task<ActionResult<PayrollAccountingPostingResponse>> Post(
        long runId, CancellationToken cancellationToken) =>
        Execute(() => sender.Send(
            new PostPayrollAccountingExpenseCommand(runId), cancellationToken));

    [HttpPost("payments/{paymentId:long}/allocations/{allocationId:long}/settlement-evidence/{evidenceId:long}/post-accounting")]
    [Authorize(Policy = "payroll.accounting.post")]
    public Task<ActionResult<PayrollAccountingPostingResponse>> PostSettlement(
        long paymentId, long allocationId, long evidenceId, CancellationToken cancellationToken) =>
        Execute(() => sender.Send(new PostPayrollAccountingSettlementCommand(
            paymentId, allocationId, evidenceId), cancellationToken));

    [HttpPost("final-settlement-payments/{paymentId:long}/settlement-evidence/{evidenceId:long}/post-accounting")]
    [Authorize(Policy = "payroll.final_settlement.accounting.post")]
    public Task<ActionResult<PayrollFinalSettlementAccountingStatus>> PostFinalSettlementPayment(
        long paymentId, long evidenceId, CancellationToken cancellationToken) =>
        Execute(() => sender.Send(new PostPayrollFinalSettlementPaymentAccountingCommand(
            paymentId, evidenceId), cancellationToken));

    [HttpGet("runs/{runId:long}/accounting-posting")]
    [Authorize(Policy = "payroll.accounting.view_posting")]
    public Task<ActionResult<PayrollAccountingPostingResponse>> Get(
        long runId, CancellationToken cancellationToken) =>
        Execute(() => sender.Send(
            new GetPayrollAccountingPostingQuery(runId), cancellationToken));

    [HttpGet("accounting-mappings")]
    [Authorize(Policy = "accounting.configuration.view")]
    public Task<ActionResult<IReadOnlyList<PayrollAccountingMappingReadModel>>> GetMappings(
        CancellationToken cancellationToken) =>
        Execute(() => sender.Send(new GetPayrollAccountingMappingsQuery(), cancellationToken));

    [HttpPut("accounting-mappings")]
    [Authorize(Policy = "accounting.configuration.edit")]
    public Task<ActionResult<PayrollAccountingMappingReadModel>> UpsertMapping(
        UpsertPayrollAccountingMappingRequest request,
        CancellationToken cancellationToken) =>
        Execute(() => sender.Send(
            new UpsertPayrollAccountingMappingCommand(
                request.ComponentType, request.ComponentCode, request.AccountId),
            cancellationToken));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll Accounting access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Payroll Accounting access denied.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll Accounting request.", detail: exception.Message);
        }
        catch (PayrollAccountingPostingConflictException exception)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll Accounting posting conflict.", detail: exception.Message);
        }
        catch (PayrollAccountingPostingException exception)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Payroll Accounting request could not be validated.", detail: exception.Message);
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
        catch (InvalidDataException exception)
        {
            logger.LogError(exception, "Payroll Accounting source evidence is invalid.");
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Payroll Accounting evidence is unavailable.");
        }
    }
}

public sealed record UpsertPayrollAccountingMappingRequest(
    string ComponentType,
    string ComponentCode,
    long AccountId);
