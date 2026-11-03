using System.Globalization;
using System.Net.Http.Headers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Services.HR;
using RetailPOS.Application.HR.Reports;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/reports/core")]
public sealed class HrCoreReportsController(
    ISender sender,
    IAuditService audit,
    HrCoreReportExportRenderer renderer,
    ILogger<HrCoreReportsController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "hr.report.view")]
    [ProducesResponseType(typeof(HrCoreReportPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HrCoreReportPage>> Query(
        [FromQuery] string kind,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? employeeId,
        [FromQuery] long? locationProjectionId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateOnly? asOfDate = null,
        [FromQuery] long? employmentTypeId = null,
        [FromQuery] string? employmentStatus = null,
        [FromQuery] long? positionId = null,
        [FromQuery] long? departmentId = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = CreateQuery(kind, from, to, employeeId, locationProjectionId, status,
                page, pageSize, asOfDate, employmentTypeId, employmentStatus, positionId,
                departmentId, sortBy, descending);
            var result = await sender.Send(query, cancellationToken);
            await RecordAccessAsync(query, result.TotalCount, "View", null, cancellationToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Core HR report access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Report access denied.", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid report filters.", detail: exception.Message);
        }
    }

    [HttpGet("export/{format}")]
    [Authorize(Policy = "hr.report.view")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(
        [FromRoute] string format,
        [FromQuery] string kind,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] long? employeeId,
        [FromQuery] long? locationProjectionId,
        [FromQuery] string? status,
        [FromQuery] DateOnly? asOfDate = null,
        [FromQuery] long? employmentTypeId = null,
        [FromQuery] string? employmentStatus = null,
        [FromQuery] long? positionId = null,
        [FromQuery] long? departmentId = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (format is not ("csv" or "xlsx"))
                throw new ArgumentException("Only CSV and XLSX exports are supported.", nameof(format));

            var query = CreateQuery(kind, from, to, employeeId, locationProjectionId, status,
                1, 200, asOfDate, employmentTypeId, employmentStatus, positionId,
                departmentId, sortBy, descending);
            var firstPage = await sender.Send(query, cancellationToken);
            if (firstPage.TotalCount > HrCoreReportExportRenderer.MaximumRows)
                throw new ArgumentException(
                    $"The report exceeds the {HrCoreReportExportRenderer.MaximumRows} row export limit.");

            var rows = new List<HrCoreReportRow>(firstPage.Items);
            var pageCount = (int)Math.Ceiling(firstPage.TotalCount / (double)query.PageSize);
            for (var page = 2; page <= pageCount; page++)
            {
                var next = await sender.Send(query with { Page = page }, cancellationToken);
                rows.AddRange(next.Items);
            }

            var settings = await sender.Send(
                new RetailPOS.Application.Settings.Queries.GetSystemSettingsQuery(), cancellationToken);
            var bytes = format == "csv"
                ? renderer.RenderCsv(rows, settings.Company.DateFormat)
                : renderer.RenderExcel(rows, settings.Company.DateFormat);
            await RecordAccessAsync(query, rows.Count, "Export", format, cancellationToken);
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.XContentTypeOptions = "nosniff";
            var extension = format == "csv" ? "csv" : "xlsx";
            var contentType = format == "csv"
                ? "text/csv; charset=utf-8"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            var fileName = $"hr-{query.Kind}-{query.From:yyyyMMdd}-{query.To:yyyyMMdd}.{extension}";
            Response.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment")
                {
                    FileNameStar = fileName
                }.ToString();
            return File(bytes, contentType, fileName);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Core HR report export access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Report access denied.", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid report export filters.", detail: exception.Message);
        }
    }

    private static HrCoreReportQuery CreateQuery(
        string kind,
        DateOnly from,
        DateOnly to,
        long? employeeId,
        long? locationProjectionId,
        string? status,
        int page,
        int pageSize,
        DateOnly? asOfDate,
        long? employmentTypeId,
        string? employmentStatus,
        long? positionId,
        long? departmentId,
        string? sortBy,
        bool descending) =>
        new(kind, from, to, employeeId, locationProjectionId, status, page, pageSize,
            asOfDate, employmentTypeId, employmentStatus, positionId, departmentId,
            sortBy, descending);

    private Task RecordAccessAsync(
        HrCoreReportQuery query,
        long rowCount,
        string action,
        string? format,
        CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditEventInput
        {
            ActionType = action == "Export" ? AuditActionType.Export : "View",
            Module = AuditModule.Reports,
            Summary = action == "Export"
                ? $"HR {query.Kind} report exported."
                : $"HR {query.Kind} report viewed.",
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
