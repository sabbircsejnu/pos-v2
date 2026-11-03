using System.Net.Http.Headers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/runs/{runId:long}/payslips/{employeeId:long}")]
public sealed class PayrollPayslipsController(ISender sender, ILogger<PayrollPayslipsController> logger)
    : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "payroll.payslip.view")]
    [ProducesResponseType<PayrollPayslipArtifactResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PayrollPayslipArtifactResponse>> Generate(
        long runId, long employeeId, CancellationToken cancellationToken) =>
        Execute<PayrollPayslipArtifactResponse>(async () => Ok(await sender.Send(
            new GeneratePayrollPayslipCommand(runId, employeeId), cancellationToken)));

    [HttpGet]
    [Authorize(Policy = "payroll.payslip.view")]
    [ProducesResponseType<PayrollPayslipArtifactResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PayrollPayslipArtifactResponse>> Get(
        long runId, long employeeId, CancellationToken cancellationToken) =>
        Execute<PayrollPayslipArtifactResponse>(async () => Ok(await sender.Send(
            new GetPayrollPayslipQuery(runId, employeeId), cancellationToken)));

    [HttpGet("download")]
    [Authorize(Policy = "payroll.payslip.download")]
    public Task<ActionResult<IActionResult>> Download(
        long runId, long employeeId, CancellationToken cancellationToken) =>
        Execute<IActionResult>(async () =>
        {
            var file = await sender.Send(
                new DownloadPayrollPayslipQuery(runId, employeeId), cancellationToken);
            SetPrivateResponseHeaders();
            Response.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment")
                {
                    FileNameStar = file.FileName
                }.ToString();
            return File(file.Content, "application/pdf");
        });

    [HttpGet("print")]
    [Authorize(Policy = "payroll.payslip.print")]
    public Task<ActionResult<IActionResult>> Print(
        long runId, long employeeId, CancellationToken cancellationToken) =>
        Execute<IActionResult>(async () =>
        {
            var file = await sender.Send(
                new DownloadPayrollPayslipQuery(runId, employeeId, true), cancellationToken);
            SetPrivateResponseHeaders();
            Response.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("inline")
                {
                    FileNameStar = file.FileName
                }.ToString();
            return File(file.Content, "application/pdf");
        });

    [HttpPost("email")]
    [Authorize(Policy = "payroll.payslip.email")]
    [ProducesResponseType<PayrollPayslipDeliveryResponse>(StatusCodes.Status200OK)]
    public Task<ActionResult<PayrollPayslipDeliveryResponse>> Email(
        long runId, long employeeId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken) =>
        Execute<PayrollPayslipDeliveryResponse>(async () => Ok(await sender.Send(
            new EmailPayrollPayslipCommand(runId, employeeId, idempotencyKey), cancellationToken)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payslip access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Payslip access denied.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (PayrollPayslipIdempotencyConflictException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Payslip email request conflicts with its idempotency key.");
        }
        catch (ArgumentException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid Payslip request.");
        }
        catch (PayrollPayslipInfrastructureUnavailableException exception)
        {
            logger.LogWarning("Payslip operation unavailable: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Confidential Payslip service is not configured.");
        }
        catch (InvalidDataException exception)
        {
            logger.LogError("Payslip artifact validation failed: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Confidential Payslip validation is unavailable.");
        }
    }

    private void SetPrivateResponseHeaders()
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
    }
}
