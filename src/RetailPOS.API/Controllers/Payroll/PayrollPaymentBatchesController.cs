using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1")]
public sealed class PayrollPaymentBatchesController(
    ISender sender,
    ILogger<PayrollPaymentBatchesController> logger) : ControllerBase
{
    [HttpPost("runs/{runId:long}/payment-batches")]
    [Authorize(Policy = "payroll.payment.process")]
    public Task<ActionResult<PayrollPaymentBatchResponse>> Create(
        long runId,
        CreatePayrollPaymentBatchRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollPaymentBatchResponse>(async () =>
        {
            var batch = await sender.Send(
                new CreatePayrollPaymentBatchCommand(runId, request), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = batch.Id }, batch);
        });

    [HttpGet("payment-batches")]
    [Authorize(Policy = "payroll.payment.view")]
    public Task<ActionResult<IReadOnlyList<PayrollPaymentBatchResponse>>> List(
        [FromQuery] long? payrollRunId,
        CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollPaymentBatchResponse>>(async () =>
            Ok(await sender.Send(
                new ListPayrollPaymentBatchesQuery(payrollRunId), cancellationToken)));

    [HttpGet("payment-batches/{id:long}")]
    [Authorize(Policy = "payroll.payment.view")]
    public Task<ActionResult<PayrollPaymentBatchResponse>> Get(
        long id, CancellationToken cancellationToken) =>
        Execute<PayrollPaymentBatchResponse>(async () =>
            Ok(await sender.Send(new GetPayrollPaymentBatchQuery(id), cancellationToken)));

    [HttpPost("payment-batches/{id:long}/submit")]
    [Authorize(Policy = "payroll.payment.process")]
    public Task<ActionResult<PayrollPaymentBatchResponse>> Submit(
        long id,
        SubmitPayrollPaymentBatchRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollPaymentBatchResponse>(async () =>
            Ok(await sender.Send(
                new SubmitPayrollPaymentBatchCommand(id, request), cancellationToken)));

    [HttpPost("payment-batches/{id:long}/generate-bank-file")]
    [Authorize(Policy = "payroll.bank_file.generate")]
    public async Task<IActionResult> GenerateBankFile(
        long id,
        PayrollPaymentBankFileRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await sender.Send(
                new GeneratePayrollPaymentBankFileCommand(id, request), cancellationToken);
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll bank-file access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Payroll bank-file access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll bank-file request.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PayrollPaymentConflictException exception)
        {
            logger.LogWarning("Payroll bank-file conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll bank-file conflict.", detail: exception.Message);
        }
    }

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll payment-batch access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Payroll payment-batch access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll payment-batch request.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PayrollPaymentConflictException exception)
        {
            logger.LogWarning("Payroll payment-batch conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll payment-batch conflict.", detail: exception.Message);
        }
    }
}
