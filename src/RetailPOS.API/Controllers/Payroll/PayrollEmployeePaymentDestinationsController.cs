using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/employees/{employeeId:long}/payment-destinations")]
public sealed class PayrollEmployeePaymentDestinationsController(
    ISender sender,
    ILogger<PayrollEmployeePaymentDestinationsController> logger) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "payroll.payment_destination.manage")]
    public Task<ActionResult<PayrollEmployeePaymentDestinationResponse>> Create(
        long employeeId, CreatePayrollEmployeePaymentDestinationRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollEmployeePaymentDestinationResponse>(async () =>
        {
            var response = await sender.Send(
                new CreatePayrollEmployeePaymentDestinationCommand(employeeId, request),
                cancellationToken);
            return CreatedAtAction(nameof(GetHistory), new { employeeId }, response);
        });

    [HttpPost("{destinationId:long}/versions")]
    [Authorize(Policy = "payroll.payment_destination.manage")]
    public Task<ActionResult<PayrollEmployeePaymentDestinationResponse>> Version(
        long employeeId, long destinationId, VersionPayrollEmployeePaymentDestinationRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollEmployeePaymentDestinationResponse>(async () => Ok(await sender.Send(
            new VersionPayrollEmployeePaymentDestinationCommand(employeeId, destinationId, request),
            cancellationToken)));

    [HttpPost("{destinationId:long}/deactivate")]
    [Authorize(Policy = "payroll.payment_destination.manage")]
    public Task<ActionResult<PayrollEmployeePaymentDestinationResponse>> Deactivate(
        long employeeId, long destinationId, DeactivatePayrollEmployeePaymentDestinationRequest request,
        CancellationToken cancellationToken) =>
        Execute<PayrollEmployeePaymentDestinationResponse>(async () => Ok(await sender.Send(
            new DeactivatePayrollEmployeePaymentDestinationCommand(employeeId, destinationId, request),
            cancellationToken)));

    [HttpGet]
    [Authorize(Policy = "payroll.payment_destination.view")]
    public Task<ActionResult<IReadOnlyList<PayrollEmployeePaymentDestinationResponse>>> GetHistory(
        long employeeId, CancellationToken cancellationToken) =>
        Execute<IReadOnlyList<PayrollEmployeePaymentDestinationResponse>>(async () => Ok(await sender.Send(
            new GetPayrollEmployeePaymentDestinationHistoryQuery(employeeId), cancellationToken)));

    [HttpGet("effective")]
    [Authorize(Policy = "payroll.payment_destination.view")]
    public Task<ActionResult<PayrollEmployeePaymentDestinationResponse>> GetEffective(
        long employeeId, [FromQuery] DateOnly? effectiveOn, CancellationToken cancellationToken) =>
        Execute<PayrollEmployeePaymentDestinationResponse>(async () => Ok(await sender.Send(
            new GetEffectivePayrollEmployeePaymentDestinationQuery(employeeId, effectiveOn),
            cancellationToken)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll payment destination access rejected: {Message}",
                exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Payroll payment destination access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll payment destination.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PayrollEmployeePaymentDestinationConflictException exception)
        {
            logger.LogWarning("Payroll payment destination conflict: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payroll payment destination conflict.", detail: exception.Message);
        }
    }
}
