using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/payments")]
public sealed class PayrollPaymentsController(
    ISender sender,
    ILogger<PayrollPaymentsController> logger) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "payroll.payment.process")]
    public Task<ActionResult<PayrollPaymentResponse>> Create(
        CreatePayrollPaymentRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollPaymentResponse>(async () =>
        {
            var payment = await sender.Send(
                new CreatePayrollPaymentCommand(request), cancellationToken);
            return CreatedAtAction(nameof(Get), new { paymentId = payment.Id }, payment);
        });

    [HttpGet("{paymentId:long}")]
    [Authorize(Policy = "payroll.payment.view")]
    public Task<ActionResult<PayrollPaymentResponse>> Get(
        long paymentId, CancellationToken cancellationToken) =>
        Execute<PayrollPaymentResponse>(async () =>
            Ok(await sender.Send(new GetPayrollPaymentQuery(paymentId), cancellationToken)));

    [HttpPost("{paymentId:long}/hold")]
    [Authorize(Policy = "payroll.payment.hold")]
    public Task<ActionResult<PayrollPaymentResponse>> Hold(
        long paymentId, PayrollPaymentStateChangeRequest request,
        CancellationToken cancellationToken) =>
        ChangeState(paymentId, request, "Hold", null, cancellationToken);

    [HttpPost("{paymentId:long}/release")]
    [Authorize(Policy = "payroll.payment.release")]
    public Task<ActionResult<PayrollPaymentResponse>> Release(
        long paymentId, PayrollPaymentStateChangeRequest request,
        CancellationToken cancellationToken) =>
        ChangeState(paymentId, request, "Release", null, cancellationToken);

    [HttpPost("{paymentId:long}/process")]
    [Authorize(Policy = "payroll.payment.process")]
    public Task<ActionResult<PayrollPaymentResponse>> Process(
        long paymentId, PayrollPaymentFailureRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollPaymentResponse>(async () =>
            Ok(await sender.Send(new ChangePayrollPaymentStateCommand(
                paymentId, request.ExpectedVersion, "Process", request.Reason,
                request.ExternalReference), cancellationToken)));

    [HttpPost("{paymentId:long}/fail")]
    [Authorize(Policy = "payroll.payment.process")]
    public Task<ActionResult<PayrollPaymentResponse>> Fail(
        long paymentId, PayrollPaymentFailureRequest request,
        CancellationToken cancellationToken) =>
        ChangeState(paymentId,
            new PayrollPaymentStateChangeRequest(request.ExpectedVersion, request.Reason),
            "Fail", request.ExternalReference, cancellationToken);

    [HttpPost("{paymentId:long}/settlements")]
    [Authorize(Policy = "payroll.payment.process")]
    public Task<ActionResult<PayrollPaymentResponse>> Settle(
        long paymentId, PayrollPaymentEvidenceRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollPaymentResponse>(async () =>
            Ok(await sender.Send(new RecordPayrollPaymentEvidenceCommand(
                paymentId, request, PayrollPaymentEvidenceTypes.Settlement), cancellationToken)));

    [HttpPost("{paymentId:long}/returns")]
    [Authorize(Policy = "payroll.payment.process")]
    public Task<ActionResult<PayrollPaymentResponse>> Return(
        long paymentId, PayrollPaymentEvidenceRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollPaymentResponse>(async () =>
            Ok(await sender.Send(new RecordPayrollPaymentEvidenceCommand(
                paymentId, request, PayrollPaymentEvidenceTypes.Return), cancellationToken)));

    [HttpPost("{paymentId:long}/retry")]
    [Authorize(Policy = "payroll.payment.retry")]
    public Task<ActionResult<PayrollPaymentResponse>> Retry(
        long paymentId, RetryPayrollPaymentRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollPaymentResponse>(async () =>
            Ok(await sender.Send(new RetryPayrollPaymentCommand(paymentId, request),
                cancellationToken)));

    private Task<ActionResult<PayrollPaymentResponse>> ChangeState(
        long paymentId,
        PayrollPaymentStateChangeRequest request,
        string operation,
        string? externalReference,
        CancellationToken cancellationToken) =>
        Execute<PayrollPaymentResponse>(async () =>
            Ok(await sender.Send(new ChangePayrollPaymentStateCommand(
                paymentId, request.ExpectedVersion, operation, request.Reason,
                externalReference), cancellationToken)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll payment access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Payroll payment access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll payment request.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PayrollPaymentConflictException exception)
        {
            logger.LogWarning("Payroll payment conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll payment conflict.", detail: exception.Message);
        }
    }
}
