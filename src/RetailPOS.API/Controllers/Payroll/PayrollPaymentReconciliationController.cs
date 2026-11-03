using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1")]
public sealed class PayrollPaymentReconciliationController(
    ISender sender,
    ILogger<PayrollPaymentReconciliationController> logger) : ControllerBase
{
    [HttpPost("payments/{paymentId:long}/reconcile")]
    [Authorize(Policy = "payroll.payment.reconcile")]
    public Task<ActionResult<PayrollPaymentReconciliationOperationResponse>> ReconcilePayment(
        long paymentId,
        ReconcilePayrollPaymentRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollPaymentReconciliationOperationResponse>(async () =>
            Ok(await sender.Send(
                new ReconcilePayrollPaymentCommand(paymentId, request), cancellationToken)));

    [HttpPost("payment-batches/{batchId:long}/reconcile")]
    [Authorize(Policy = "payroll.payment.reconcile")]
    public Task<ActionResult<PayrollPaymentReconciliationOperationResponse>> ReconcileBatch(
        long batchId,
        ReconcilePayrollPaymentBatchRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollPaymentReconciliationOperationResponse>(async () =>
            Ok(await sender.Send(
                new ReconcilePayrollPaymentBatchCommand(batchId, request), cancellationToken)));

    [HttpGet("payment-reconciliation")]
    [Authorize(Policy = "payroll.payment.view")]
    public Task<ActionResult<IReadOnlyList<PayrollPaymentReconciliationResponse>>> List(
        [FromQuery] long? paymentId,
        [FromQuery] long? paymentBatchId,
        CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollPaymentReconciliationResponse>>(async () =>
            Ok(await sender.Send(
                new ListPayrollPaymentReconciliationsQuery(paymentId, paymentBatchId),
                cancellationToken)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll payment reconciliation access rejected: {Message}",
                exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Payroll payment reconciliation access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll payment reconciliation request.",
                detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PayrollPaymentConflictException exception)
        {
            logger.LogWarning("Payroll payment reconciliation conflict: {Message}",
                exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll payment reconciliation conflict.", detail: exception.Message);
        }
    }
}
