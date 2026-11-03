using System.Globalization;
using System.Net.Http.Headers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Services.Payroll;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Controllers.Payroll;

[ApiController]
[Authorize]
[Route("api/payroll/v1/reports/advanced")]
public sealed class PayrollAdvancedReportsController(
    ISender sender,
    IAuditService audit,
    PayrollAdvancedReportExportRenderer renderer,
    ILogger<PayrollAdvancedReportsController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "payroll.report.view")]
    [Authorize(Policy = "payroll.salary.view")]
    [ProducesResponseType(typeof(PayrollAdvancedReportPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PayrollAdvancedReportPage>> Query(
        [FromQuery] string kind,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? employeeId,
        [FromQuery] long? locationId,
        [FromQuery] long? runId,
        [FromQuery] long? compareRunId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new PayrollAdvancedReportQuery(
                kind, from, to, employeeId, locationId, runId, compareRunId, page, pageSize);
            var result = await sender.Send(query, cancellationToken);
            await RecordAccessAsync(query, result.TotalCount, null, cancellationToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll report access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Payroll report access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll report filters.", detail: exception.Message);
        }
    }

    [HttpGet("export/{format}")]
    [Authorize(Policy = "payroll.report.view")]
    [Authorize(Policy = "payroll.salary.view")]
    [Authorize(Policy = "payroll.export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(
        [FromRoute] string format,
        [FromQuery] string kind,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? employeeId,
        [FromQuery] long? locationId,
        [FromQuery] long? runId,
        [FromQuery] long? compareRunId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (format is not ("csv" or "xlsx"))
                throw new ArgumentException("Only CSV and XLSX exports are supported.", nameof(format));
            var query = new PayrollAdvancedReportQuery(
                kind, from, to, employeeId, locationId, runId, compareRunId,
                PageSize: PayrollAdvancedReportLimits.MaximumPageSize);
            var firstPage = await sender.Send(query, cancellationToken);
            if (firstPage.TotalCount > PayrollAdvancedReportLimits.MaximumExportRows)
                throw new ArgumentException(
                    $"The report exceeds the {PayrollAdvancedReportLimits.MaximumExportRows} row export limit.");

            var rows = new List<PayrollAdvancedReportRow>(firstPage.Items);
            var pageCount = (int)Math.Ceiling(firstPage.TotalCount / (double)query.PageSize);
            for (var page = 2; page <= pageCount; page++)
                rows.AddRange((await sender.Send(query with { Page = page }, cancellationToken)).Items);

            var settings = await sender.Send(
                new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken);
            var bytes = format == "csv" ? renderer.RenderCsv(rows, settings.Company.DateFormat)
                : renderer.RenderExcel(rows, settings.Company.DateFormat);
            await RecordAccessAsync(query, rows.Count, format, cancellationToken);
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.XContentTypeOptions = "nosniff";
            var extension = format == "csv" ? "csv" : "xlsx";
            var contentType = format == "csv"
                ? "text/csv; charset=utf-8"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            var fileName = $"payroll-{query.Kind}-{query.From:yyyyMMdd}-{query.To:yyyyMMdd}.{extension}";
            Response.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName }.ToString();
            return File(bytes, contentType, fileName);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Payroll report export access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Payroll report access denied.");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Payroll report export filters.", detail: exception.Message);
        }
    }

    private Task RecordAccessAsync(
        PayrollAdvancedReportQuery query,
        long rowCount,
        string? format,
        CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditEventInput
        {
            ActionType = format is null ? "View" : AuditActionType.Export,
            Module = AuditModule.Reports,
            Summary = format is null
                ? $"Payroll {query.Kind} report viewed."
                : $"Payroll {query.Kind} report exported.",
            Metadata = new Dictionary<string, object?>
            {
                ["ReportKind"] = query.Kind,
                ["From"] = query.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["To"] = query.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["RowCount"] = rowCount,
                ["Format"] = format
            }
        }, cancellationToken);
}
