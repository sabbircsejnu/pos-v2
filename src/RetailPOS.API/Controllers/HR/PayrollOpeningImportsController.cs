using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Employees;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/payroll/v1/opening-imports")]
public sealed class PayrollOpeningImportsController(
    IHrOpeningImportService imports, ILogger<PayrollOpeningImportsController> logger) : ControllerBase
{
    [Authorize(Policy = "payroll.import.view")]
    [HttpGet("template")]
    public async Task<IActionResult> Template([FromQuery] string importType = "Salary",
        [FromQuery] string templateVersion = "1", CancellationToken cancellationToken = default) =>
        await RunAsync(async () =>
        {
            var template = await imports.TemplateAsync(importType, templateVersion, cancellationToken);
            Response.Headers.CacheControl = "private, no-store";
            return File(Encoding.UTF8.GetBytes(template), "text/csv", $"opening-{importType}-v{templateVersion}.csv");
        });

    [Authorize(Policy = "payroll.import.execute")]
    [HttpPost]
    [RequestSizeLimit(10_485_760)]
    public async Task<IActionResult> Upload([FromForm] HrOpeningImportUploadRequest request,
        CancellationToken cancellationToken) => await RunAsync(async () =>
    {
        if (request.File is null || request.File.Length == 0)
            return BadRequest(new { message = "CSV file is required." });
        await using var stream = request.File.OpenReadStream();
        var result = await imports.UploadAsync(request.ImportType, request.TemplateVersion,
            request.IdempotencyKey, request.File.FileName, request.File.ContentType, stream, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    });

    [Authorize(Policy = "payroll.import.view")]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken) =>
        await RunAsync(async () =>
        {
            Response.Headers.CacheControl = "private, no-store";
            return Ok(await imports.GetAsync(id, cancellationToken));
        });

    [Authorize(Policy = "payroll.import.execute")]
    [HttpPost("{id:long}/dry-run")]
    public async Task<IActionResult> DryRun(long id, CancellationToken cancellationToken) =>
        await RunAsync(async () => Ok(await imports.ValidateAsync(id, cancellationToken)));

    [Authorize(Policy = "payroll.import.execute")]
    [HttpPost("{id:long}/execute")]
    public async Task<IActionResult> Execute(long id, CancellationToken cancellationToken) =>
        await RunAsync(async () => Ok(await imports.ExecuteAsync(id, cancellationToken)));

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        try { return await action(); }
        catch (HrImportStorageUnavailableException exception)
        {
            logger.LogWarning("Opening Payroll import storage composition is unavailable.");
            return StatusCode(503, new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Opening Payroll import access denied.");
            return StatusCode(403, new { message = exception.Message });
        }
        catch (ConfidentialFileScanException exception)
        {
            logger.LogWarning("Opening Payroll import scan denied.");
            return UnprocessableEntity(new { message = exception.Message, scanStatus = exception.Status.ToString() });
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning("Opening Payroll import input was rejected.");
            return BadRequest(new { message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            logger.LogWarning("Opening Payroll import was not found in the active Business.");
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("Opening Payroll import request conflicted.");
            return Conflict(new { message = exception.Message });
        }
    }
}
