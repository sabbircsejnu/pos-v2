using System.Net.Http.Headers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/runs/{runId:long}/exports")]
public sealed class PayrollExportsController(
    ISender sender,
    ILogger<PayrollExportsController> logger) : ControllerBase
{
    [HttpGet("csv")]
    [Authorize(Policy = "payroll.export")]
    public Task<IActionResult> Csv(long runId, CancellationToken cancellationToken) =>
        Export(async () => await sender.Send(
            new ExportPayrollRunCsvQuery(runId), cancellationToken));

    [HttpGet("excel")]
    [Authorize(Policy = "payroll.export")]
    public Task<IActionResult> Excel(long runId, CancellationToken cancellationToken) =>
        Export(async () => await sender.Send(
            new ExportPayrollRunExcelQuery(runId), cancellationToken));

    [HttpGet("pdf-summary")]
    [Authorize(Policy = "payroll.export")]
    public Task<IActionResult> PdfSummary(long runId, CancellationToken cancellationToken) =>
        Export(async () => await sender.Send(
            new ExportPayrollRunPdfSummaryQuery(runId), cancellationToken));

    private async Task<IActionResult> Export(Func<Task<PayrollExportDownload>> action)
    {
        try
        {
            var download = await action();
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.XContentTypeOptions = "nosniff";
            Response.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment")
                {
                    FileNameStar = download.FileName
                }.ToString();
            return File(download.Content, download.ContentType);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll export access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Payroll export access denied.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "Payroll export request was invalid.");
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid Payroll export request.");
        }
        catch (InvalidDataException exception)
        {
            logger.LogError("Payroll export evidence validation failed: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Payroll export is unavailable.");
        }
    }
}
