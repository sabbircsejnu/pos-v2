using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Infrastructure.HR.Employees;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1")]
public sealed class HrEmployeesController(
    ISender sender,
    ILogger<HrEmployeesController> logger) : ControllerBase
{
    [HttpGet("employment-types")]
    [Authorize(Policy = "hr.employee.view")]
    public async Task<ActionResult<IReadOnlyList<HrEmploymentTypeReadModel>>> GetEmploymentTypes(
        CancellationToken cancellationToken) =>
        await ExecuteAsync<IReadOnlyList<HrEmploymentTypeReadModel>>(async () =>
            Ok(await sender.Send(new GetHrEmploymentTypesQuery(), cancellationToken)));

    [HttpGet("employees")]
    [Authorize(Policy = "hr.employee.view")]
    public async Task<ActionResult<HrEmployeeSearchPage>> SearchEmployees(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync<HrEmployeeSearchPage>(async () =>
            Ok(await sender.Send(new SearchHrEmployeesQuery(search, page, pageSize), cancellationToken)));

    [HttpGet("employees/{id:long}")]
    [Authorize(Policy = "hr.employee.view")]
    public async Task<ActionResult<HrEmployeeProfile>> GetEmployee(
        long id, CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeProfile>(async () =>
        {
            var profile = await sender.Send(new GetHrEmployeeProfileQuery(id), cancellationToken);
            return profile is null ? NotFound() : Ok(profile);
        });

    [HttpGet("employees/{id:long}/user-link")]
    [Authorize(Policy = "hr.employee.view")]
    [ProducesResponseType(typeof(HrEmployeeUserLinkState), StatusCodes.Status200OK)]
    public async Task<ActionResult<HrEmployeeUserLinkState>> GetEmployeeUserLink(
        long id, CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeUserLinkState>(async () =>
            Ok(await sender.Send(new GetHrEmployeeUserLinkQuery(id), cancellationToken)));

    [HttpPost("employees/{id:long}/user-link")]
    [Authorize(Policy = "hr.employee.link_user")]
    [ProducesResponseType(typeof(HrEmployeeUserLinkReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<HrEmployeeUserLinkReadModel>> LinkEmployeeUser(
        long id, [FromBody] LinkHrEmployeeUserRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeUserLinkReadModel>(async () =>
        {
            var linked = await sender.Send(
                new LinkHrEmployeeUserCommand(id, request.IdentityUserId, request.Reason), cancellationToken);
            return linked is null ? NotFound() : Ok(linked);
        });

    [HttpDelete("employees/{id:long}/user-link")]
    [Authorize(Policy = "hr.employee.unlink_user")]
    [ProducesResponseType(typeof(HrEmployeeUserLinkReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<HrEmployeeUserLinkReadModel>> UnlinkEmployeeUser(
        long id, [FromBody] UnlinkHrEmployeeUserRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeUserLinkReadModel>(async () =>
        {
            var unlinked = await sender.Send(
                new UnlinkHrEmployeeUserCommand(id, request.ExpectedVersion, request.Reason), cancellationToken);
            return unlinked is null ? NotFound() : Ok(unlinked);
        });

    [HttpGet("me/employee")]
    [ProducesResponseType(typeof(HrEmployeeSelfReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HrEmployeeSelfReadModel>> GetCurrentEmployee(
        CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeSelfReadModel>(async () =>
        {
            var employee = await sender.Send(new GetCurrentHrEmployeeQuery(), cancellationToken);
            return employee is null ? NotFound() : Ok(employee);
        });

    [HttpPost("employees")]
    [Authorize(Policy = "hr.employee.create")]
    public async Task<ActionResult<HrEmployeeProfile>> CreateEmployee(
        [FromBody] HrEmployeeCreateData data, CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeProfile>(async () =>
        {
            var created = await sender.Send(new CreateHrEmployeeCommand(data), cancellationToken);
            return CreatedAtAction(nameof(GetEmployee), new { id = created.Id }, created);
        });

    [HttpPut("employees/{id:long}")]
    [Authorize(Policy = "hr.employee.edit")]
    public async Task<ActionResult<HrEmployeeProfile>> UpdateEmployee(
        long id, [FromBody] UpdateHrEmployeeRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeProfile>(async () =>
        {
            var updated = await sender.Send(
                new UpdateHrEmployeeCommand(id, request.ExpectedVersion, request.Data), cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        });

    [HttpGet("employees/{id:long}/lifecycle")]
    [Authorize(Policy = "hr.employee_lifecycle.view")]
    [ProducesResponseType(typeof(IReadOnlyList<HrEmployeeLifecycleEventReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HrEmployeeLifecycleEventReadModel>>> GetLifecycleHistory(
        long id, CancellationToken cancellationToken) =>
        await ExecuteAsync<IReadOnlyList<HrEmployeeLifecycleEventReadModel>>(async () =>
            Ok(await sender.Send(new GetHrEmployeeLifecycleHistoryQuery(id), cancellationToken)));

    [HttpPost("employees/{id:long}/lifecycle")]
    [Authorize(Policy = "hr.employee_lifecycle.manage")]
    [ProducesResponseType(typeof(HrEmployeeLifecycleEventReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<HrEmployeeLifecycleEventReadModel>> TransitionLifecycle(
        long id, [FromBody] TransitionHrEmployeeLifecycleRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeLifecycleEventReadModel>(async () =>
        {
            var result = await sender.Send(
                new TransitionHrEmployeeLifecycleCommand(id, request.ExpectedVersion, request.Data),
                cancellationToken);
            return result is null ? NotFound() : Ok(result);
        });

    [HttpPost("employees/{id:long}/rehire")]
    [Authorize(Policy = "hr.employee_lifecycle.rehire")]
    [ProducesResponseType(typeof(HrEmployeeLifecycleEventReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<HrEmployeeLifecycleEventReadModel>> Rehire(
        long id, [FromBody] RehireHrEmployeeRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeLifecycleEventReadModel>(async () =>
        {
            var result = await sender.Send(
                new RehireHrEmployeeCommand(id, request.ExpectedVersion, request.Data), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        });

    [HttpGet("employees/{id:long}/positions")]
    [Authorize(Policy = "hr.position.view")]
    [ProducesResponseType(typeof(IReadOnlyList<HrEmployeePositionAssignmentReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HrEmployeePositionAssignmentReadModel>>> GetPositionHistory(
        long id, CancellationToken cancellationToken) =>
        await ExecuteAsync<IReadOnlyList<HrEmployeePositionAssignmentReadModel>>(async () =>
            Ok(await sender.Send(new GetHrEmployeePositionHistoryQuery(id), cancellationToken)));

    [HttpPost("employees/{id:long}/positions")]
    [Authorize(Policy = "hr.position.manage")]
    [ProducesResponseType(typeof(HrEmployeePositionAssignmentReadModel), StatusCodes.Status200OK)]
    public async Task<ActionResult<HrEmployeePositionAssignmentReadModel>> AssignPosition(
        long id, [FromBody] AssignHrEmployeePositionRequest request, CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeePositionAssignmentReadModel>(async () =>
        {
            var result = await sender.Send(
                new AssignHrEmployeePositionCommand(id, request.ExpectedVersion, request.Data),
                cancellationToken);
            return result is null ? NotFound() : Ok(result);
        });

    [HttpGet("employees/{id:long}/locations")]
    [Authorize(Policy = "hr.employee_transfer.view")]
    [ProducesResponseType(typeof(IReadOnlyList<HrEmployeeLocationAssignmentReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HrEmployeeLocationAssignmentReadModel>>> GetLocationHistory(
        long id,
        [FromQuery] DateOnly? effectiveOn,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<IReadOnlyList<HrEmployeeLocationAssignmentReadModel>>(async () =>
        {
            var history = await sender.Send(new GetHrEmployeeLocationHistoryQuery(id), cancellationToken);
            var effectiveDate = effectiveOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
            return Ok(history.Where(item => item.EffectiveFrom <= effectiveDate
                && (!item.EffectiveTo.HasValue || item.EffectiveTo >= effectiveDate)
                || effectiveOn is null).ToArray());
        });

    [HttpGet("employees/{id:long}/location-transfers")]
    [Authorize(Policy = "hr.employee_transfer.view")]
    [ProducesResponseType(typeof(IReadOnlyList<HrEmployeeLocationTransferReadModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HrEmployeeLocationTransferReadModel>>> GetLocationTransfers(
        long id,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<IReadOnlyList<HrEmployeeLocationTransferReadModel>>(async () =>
            Ok(await sender.Send(new GetHrEmployeeLocationTransfersQuery(id), cancellationToken)));

    [HttpPost("employees/{id:long}/location-assignments")]
    [Authorize(Policy = "hr.employee_transfer.create")]
    [ProducesResponseType(typeof(HrEmployeeLocationAssignmentReadModel), StatusCodes.Status201Created)]
    public async Task<ActionResult<HrEmployeeLocationAssignmentReadModel>> AssignLocation(
        long id,
        [FromBody] AssignHrEmployeeLocationRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeLocationAssignmentReadModel>(async () =>
        {
            var assignment = await sender.Send(new AssignHrEmployeeLocationCommand(
                id, request.ExpectedEmployeeVersion, request.Data), cancellationToken);
            return assignment is null
                ? NotFound()
                : CreatedAtAction(nameof(GetLocationHistory), new { id }, assignment);
        });

    [HttpPost("employees/{id:long}/location-transfers")]
    [Authorize(Policy = "hr.employee_transfer.create")]
    [ProducesResponseType(typeof(HrEmployeeLocationTransferSubmissionResult), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<HrEmployeeLocationTransferSubmissionResult>> RequestLocationTransfer(
        long id,
        [FromBody] RequestHrEmployeeLocationTransferRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeLocationTransferSubmissionResult>(async () =>
        {
            var submitted = await sender.Send(new RequestHrEmployeeLocationTransferCommand(id, request.Data),
                cancellationToken);
            return Accepted($"/api/hr/v1/employees/{id}/locations", submitted);
        });

    [HttpPost("employees/{id:long}/location-transfers/{transferId:guid}/approve")]
    [Authorize(Policy = "hr.employee_transfer.approve")]
    [ProducesResponseType(typeof(RetailPOS.Application.HR.Approvals.ApprovalDecisionResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<RetailPOS.Application.HR.Approvals.ApprovalDecisionResult>> ApproveLocationTransfer(
        long id,
        Guid transferId,
        [FromBody] DecideHrEmployeeLocationTransferRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<RetailPOS.Application.HR.Approvals.ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new DecideHrEmployeeLocationTransferCommand(
                id, transferId, request.DecisionKey, request.Reason, Approve: true), cancellationToken)));

    [HttpPost("employees/{id:long}/location-transfers/{transferId:guid}/reject")]
    [Authorize(Policy = "hr.employee_transfer.approve")]
    [ProducesResponseType(typeof(RetailPOS.Application.HR.Approvals.ApprovalDecisionResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<RetailPOS.Application.HR.Approvals.ApprovalDecisionResult>> RejectLocationTransfer(
        long id,
        Guid transferId,
        [FromBody] DecideHrEmployeeLocationTransferRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<RetailPOS.Application.HR.Approvals.ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new DecideHrEmployeeLocationTransferCommand(
                id, transferId, request.DecisionKey, request.Reason, Approve: false), cancellationToken)));

    [HttpPost("employees/{id:long}/location-transfers/{transferId:guid}/cancel")]
    [Authorize(Policy = "hr.employee_transfer.create")]
    [ProducesResponseType(typeof(RetailPOS.Application.HR.Approvals.ApprovalDecisionResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<RetailPOS.Application.HR.Approvals.ApprovalDecisionResult>> CancelLocationTransfer(
        long id,
        Guid transferId,
        [FromBody] CancelHrEmployeeLocationTransferRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<RetailPOS.Application.HR.Approvals.ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new CancelHrEmployeeLocationTransferCommand(
                id, transferId, request.DecisionKey, request.Reason), cancellationToken)));

    [HttpGet("employee-document-types")]
    [Authorize(Policy = "hr.employee_document.view")]
    public async Task<ActionResult<IReadOnlyList<HrEmployeeDocumentTypeReadModel>>> GetEmployeeDocumentTypes(
        CancellationToken cancellationToken) =>
        await ExecuteAsync<IReadOnlyList<HrEmployeeDocumentTypeReadModel>>(async () =>
            Ok(await sender.Send(new GetHrEmployeeDocumentTypesQuery(), cancellationToken)));

    [HttpPost("employee-document-types")]
    [Authorize(Policy = "hr.settings.manage")]
    public async Task<ActionResult<HrEmployeeDocumentTypeReadModel>> CreateEmployeeDocumentType(
        [FromBody] HrEmployeeDocumentTypeData data,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeDocumentTypeReadModel>(async () =>
            Ok(await sender.Send(new CreateHrEmployeeDocumentTypeCommand(data), cancellationToken)));

    [HttpGet("employees/{id:long}/documents")]
    [Authorize(Policy = "hr.employee_document.view")]
    public async Task<ActionResult<IReadOnlyList<HrEmployeeDocumentReadModel>>> GetEmployeeDocuments(
        long id, CancellationToken cancellationToken) =>
        await ExecuteAsync<IReadOnlyList<HrEmployeeDocumentReadModel>>(async () =>
            Ok(await sender.Send(new GetHrEmployeeDocumentsQuery(id), cancellationToken)));

    [HttpPost("employees/{id:long}/documents")]
    [Authorize(Policy = "hr.employee_document.upload")]
    [RequestSizeLimit(30_000_000)]
    public async Task<ActionResult<HrEmployeeDocumentReadModel>> UploadEmployeeDocument(
        long id,
        [FromForm] HrEmployeeDocumentUploadRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeDocumentReadModel>(async () =>
        {
            if (request.File is null)
                throw new ArgumentException("An uploaded file is required.");
            await using var content = request.File.OpenReadStream();
            var result = await sender.Send(new CreateHrEmployeeDocumentCommand(
                id, request.DocumentTypeId, request.DocumentNumber, request.IssueDate, request.ExpiryDate,
                request.Notes, request.IsLegalHold, request.File.FileName, request.File.ContentType,
                content), cancellationToken);
            return Ok(result);
        });

    [HttpPost("employee-documents/{id:long}/versions")]
    [Authorize(Policy = "hr.employee_document.replace")]
    [RequestSizeLimit(30_000_000)]
    public async Task<ActionResult<HrEmployeeDocumentReadModel>> ReplaceEmployeeDocumentVersion(
        long id,
        [FromForm] HrEmployeeDocumentVersionUploadRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeDocumentReadModel>(async () =>
        {
            if (request.File is null)
                throw new ArgumentException("An uploaded file is required.");
            await using var content = request.File.OpenReadStream();
            var result = await sender.Send(new ReplaceHrEmployeeDocumentVersionCommand(
                id, request.ExpectedDocumentVersion, request.File.FileName, request.File.ContentType,
                content), cancellationToken);
            return Ok(result);
        });

    [HttpGet("employee-documents/{id:long}/download")]
    [Authorize(Policy = "hr.employee_document.download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadEmployeeDocument(
        long id, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync<AuthorizedConfidentialFile>(async () =>
            Ok(await sender.Send(new DownloadHrEmployeeDocumentQuery(id), cancellationToken)));
        if (result.Result is not OkObjectResult { Value: AuthorizedConfidentialFile file })
            return result.Result!;
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
    }

    [HttpPost("employee-documents/{id:long}/archive")]
    [Authorize(Policy = "hr.employee_document.delete")]
    public async Task<ActionResult<HrEmployeeDocumentReadModel?>> ArchiveEmployeeDocument(
        long id,
        [FromBody] ArchiveHrEmployeeDocumentRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync<HrEmployeeDocumentReadModel?>(async () =>
            Ok(await sender.Send(new ArchiveHrEmployeeDocumentCommand(
                id, request.ExpectedVersion, request.Reason), cancellationToken)));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<ActionResult<T>>> action)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("HR employee request rejected: {Message}", exception.Message);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
        catch (ConfidentialStorageUnavailableException exception)
        {
            logger.LogWarning("Confidential document storage is unavailable: {Message}", exception.Message);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (ConfidentialFileScanException exception)
        {
            return UnprocessableEntity(new { message = exception.Message, scanStatus = exception.Status.ToString() });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (HrEmployeeConcurrencyException)
        {
            return Conflict(new { message = "Employee was changed by another request." });
        }
        catch (HrEmployeeDocumentConcurrencyException)
        {
            return Conflict(new { message = "Employee document was changed by another request." });
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("HR employee operation conflicted: {Message}", exception.Message);
            return Conflict(new { message = exception.Message });
        }
    }
}

public sealed record UpdateHrEmployeeRequest(long ExpectedVersion, HrEmployeeUpdateData Data);
public sealed record TransitionHrEmployeeLifecycleRequest(
    long ExpectedVersion, HrEmployeeLifecycleTransitionData Data);
public sealed record RehireHrEmployeeRequest(long ExpectedVersion, HrEmployeeRehireData Data);
public sealed record AssignHrEmployeePositionRequest(
    long ExpectedVersion, HrEmployeePositionAssignmentData Data);

/// <summary>Payload for creating an initial or additional employee location assignment.</summary>
public sealed record AssignHrEmployeeLocationRequest(
    long ExpectedEmployeeVersion,
    HrEmployeeLocationAssignmentData Data);

/// <summary>Payload for submitting a Primary location transfer for approval.</summary>
public sealed record RequestHrEmployeeLocationTransferRequest(HrEmployeeLocationTransferData Data);

/// <summary>Payload for recording an approval or rejection of a location transfer.</summary>
public sealed record DecideHrEmployeeLocationTransferRequest(string DecisionKey, string Reason);

/// <summary>Payload for cancelling a pending location transfer approval.</summary>
public sealed record CancelHrEmployeeLocationTransferRequest(string DecisionKey, string Reason);

/// <summary>Payload for linking an existing Login User to an employee.</summary>
public sealed record LinkHrEmployeeUserRequest(long IdentityUserId, string Reason);

/// <summary>Payload for closing an active employee/Login User link.</summary>
public sealed record UnlinkHrEmployeeUserRequest(long ExpectedVersion, string Reason);
public sealed record HrEmployeeDocumentUploadRequest(
    long DocumentTypeId,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    string? Notes,
    bool IsLegalHold,
    IFormFile File);
public sealed record HrEmployeeDocumentVersionUploadRequest(long ExpectedDocumentVersion, IFormFile File);
public sealed record ArchiveHrEmployeeDocumentRequest(long ExpectedVersion, string Reason);
