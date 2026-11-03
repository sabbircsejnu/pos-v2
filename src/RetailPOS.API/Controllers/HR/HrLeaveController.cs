using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.API.Controllers.HR;

[ApiController]
[Authorize]
[Route("api/hr/v1/leave")]
public sealed class HrLeaveController(ISender sender, ILogger<HrLeaveController> logger) : ControllerBase
{
    [HttpGet("requests/mine")]
    [Authorize(Policy = "hr.leave.request")]
    public Task<ActionResult<IReadOnlyList<HrLeaveRequestModel>>> GetMyRequests(CancellationToken ct) =>
        Execute<IReadOnlyList<HrLeaveRequestModel>>(async () =>
            Ok(await sender.Send(new GetMyHrLeaveRequestsQuery(), ct)));

    [HttpGet("requests")]
    [Authorize]
    public Task<ActionResult<IReadOnlyList<HrLeaveRequestModel>>> GetRequests(
        [FromQuery] long? employeeId, [FromQuery] string? status, CancellationToken ct) =>
        Execute<IReadOnlyList<HrLeaveRequestModel>>(async () =>
            Ok(await sender.Send(new GetHrLeaveRequestsQuery(employeeId, status), ct)));

    [HttpGet("requests/{requestId:long}", Name = nameof(GetRequest))]
    [Authorize]
    public Task<ActionResult<HrLeaveRequestModel>> GetRequest(long requestId, CancellationToken ct) =>
        Execute<HrLeaveRequestModel>(async () =>
        {
            var result = await sender.Send(new GetHrLeaveRequestQuery(requestId), ct);
            return result is null ? NotFound() : Ok(result);
        });

    [HttpPost("requests")]
    [Authorize(Policy = "hr.leave.request")]
    public Task<ActionResult<HrLeaveRequestModel>> CreateRequest(
        [FromBody] CreateLeaveRequestBody body, CancellationToken ct) =>
        Execute<HrLeaveRequestModel>(async () =>
        {
            var created = await sender.Send(new CreateHrLeaveRequestDraftCommand(
                new HrLeaveRequestDraftInput(body.LeaveTypeId, body.StartDate, body.EndDate, body.Reason)), ct);
            return CreatedAtAction(nameof(GetRequest), new { requestId = created.Id }, created);
        });

    [HttpPost("requests/{requestId:long}/submit")]
    [Authorize(Policy = "hr.leave.request")]
    public Task<ActionResult<ApprovalDecisionResult>> SubmitRequest(
        long requestId, [FromBody] SubmitLeaveRequestBody body, CancellationToken ct) =>
        Execute<ApprovalDecisionResult>(async () =>
            Accepted($"/api/hr/v1/leave/requests/{requestId}",
                await sender.Send(new SubmitHrLeaveRequestCommand(requestId, body.ExpectedVersion), ct)));

    [HttpPost("requests/{requestId:long}/cancel")]
    [Authorize(Policy = "hr.leave.request")]
    public Task<ActionResult<HrLeaveRequestModel>> CancelRequest(
        long requestId, [FromBody] CancelLeaveRequestBody body, CancellationToken ct) =>
        Execute<HrLeaveRequestModel>(async () =>
        {
            var result = await sender.Send(new CancelHrLeaveRequestCommand(
                requestId, body.DecisionKey, body.Reason), ct);
            return result is null ? NotFound() : Ok(result);
        });

    [HttpGet("requests/{requestId:long}/attachments")]
    [Authorize]
    public Task<ActionResult<IReadOnlyList<HrLeaveRequestAttachmentModel>>> GetRequestAttachments(
        long requestId, CancellationToken ct) =>
        Execute<IReadOnlyList<HrLeaveRequestAttachmentModel>>(async () =>
        {
            var attachments = await sender.Send(new GetHrLeaveRequestAttachmentsQuery(requestId), ct);
            SetPrivateNoStoreHeaders();
            return Ok(attachments);
        });

    [HttpPost("requests/{requestId:long}/attachments")]
    [Authorize(Policy = "hr.leave.request")]
    [Consumes("multipart/form-data")]
    public Task<ActionResult<HrLeaveRequestAttachmentModel>> UploadRequestAttachment(
        long requestId, [FromForm] UploadLeaveAttachmentBody body, CancellationToken ct) =>
        Execute<HrLeaveRequestAttachmentModel>(async () =>
        {
            if (body.File is null || body.File.Length <= 0)
                throw new ArgumentException("A non-empty attachment file is required.");
            await using var stream = body.File.OpenReadStream();
            return Ok(await sender.Send(new UploadHrLeaveRequestAttachmentCommand(
                requestId, body.File.FileName, body.File.ContentType, stream), ct));
        });

    [HttpGet("requests/{requestId:long}/attachments/{versionId:guid}")]
    [Authorize]
    public async Task<IActionResult> DownloadRequestAttachment(
        long requestId, Guid versionId, CancellationToken ct)
    {
        var result = await Execute<AuthorizedConfidentialFile>(async () =>
            Ok(await sender.Send(new DownloadHrLeaveRequestAttachmentQuery(requestId, versionId), ct)));
        if (result.Result is not OkObjectResult { Value: AuthorizedConfidentialFile file })
            return result.Result!;
        SetPrivateNoStoreHeaders();
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
    }

    [HttpPost("approvals/{instanceId:long}/approve")]
    [Authorize(Policy = "hr.leave.approve")]
    public Task<ActionResult<ApprovalDecisionResult>> ApproveRequest(
        long instanceId, [FromBody] DecideLeaveRequestBody body, CancellationToken ct) =>
        DecideRequest(instanceId, body, ApprovalDecisionType.Approve, ct);

    [HttpPost("approvals/{instanceId:long}/reject")]
    [Authorize(Policy = "hr.leave.approve")]
    public Task<ActionResult<ApprovalDecisionResult>> RejectRequest(
        long instanceId, [FromBody] DecideLeaveRequestBody body, CancellationToken ct) =>
        DecideRequest(instanceId, body, ApprovalDecisionType.Reject, ct);

    private Task<ActionResult<ApprovalDecisionResult>> DecideRequest(
        long instanceId, DecideLeaveRequestBody body, ApprovalDecisionType decision, CancellationToken ct) =>
        Execute<ApprovalDecisionResult>(async () =>
            Ok(await sender.Send(new RecordApprovalDecisionCommand(
                instanceId, body.DecisionKey, decision, body.Reason), ct)));

    [HttpGet("types")]
    [Authorize]
    public Task<ActionResult<IReadOnlyList<HrLeaveTypeModel>>> GetTypes(CancellationToken ct) =>
        Execute<IReadOnlyList<HrLeaveTypeModel>>(async () =>
            Ok(await sender.Send(new GetHrLeaveTypesQuery(), ct)));

    [HttpPost("types")]
    [Authorize(Policy = "hr.leave.admin")]
    public Task<ActionResult<HrLeaveTypeModel>> CreateType(HrLeaveTypeInput input, CancellationToken ct) =>
        Execute<HrLeaveTypeModel>(async () =>
        {
            var created = await sender.Send(new CreateHrLeaveTypeCommand(input), ct);
            return CreatedAtAction(nameof(GetTypes), new { }, created);
        });

    [HttpGet("policies")]
    [Authorize(Policy = "hr.leave.view")]
    public Task<ActionResult<IReadOnlyList<HrLeavePolicyModel>>> GetPolicies(
        [FromQuery] long? leaveTypeId, CancellationToken ct) =>
        Execute<IReadOnlyList<HrLeavePolicyModel>>(async () =>
            Ok(await sender.Send(new GetHrLeavePoliciesQuery(leaveTypeId), ct)));

    [HttpPost("policies")]
    [Authorize(Policy = "hr.leave.admin")]
    public Task<ActionResult<HrLeavePolicyModel>> CreatePolicyVersion(
        HrLeavePolicyInput input, CancellationToken ct) =>
        Execute<HrLeavePolicyModel>(async () =>
        {
            var created = await sender.Send(new CreateHrLeavePolicyVersionCommand(input), ct);
            return CreatedAtAction(nameof(GetPolicies), new { leaveTypeId = created.LeaveTypeId }, created);
        });

    [HttpGet("employees/{employeeId:long}/balances")]
    [Authorize(Policy = "hr.leave_balance.view")]
    public Task<ActionResult<IReadOnlyList<HrLeaveBalanceModel>>> GetBalances(
        long employeeId, [FromQuery] long? leaveTypeId, CancellationToken ct) =>
        Execute<IReadOnlyList<HrLeaveBalanceModel>>(async () =>
            Ok(await sender.Send(new GetHrLeaveBalancesQuery(employeeId, leaveTypeId), ct)));

    [HttpGet("employees/{employeeId:long}/ledger")]
    [Authorize(Policy = "hr.leave_balance.view")]
    public Task<ActionResult<IReadOnlyList<HrLeaveLedgerModel>>> GetLedger(
        long employeeId, [FromQuery] long? leaveTypeId, CancellationToken ct) =>
        Execute<IReadOnlyList<HrLeaveLedgerModel>>(async () =>
            Ok(await sender.Send(new GetHrLeaveLedgerQuery(employeeId, leaveTypeId), ct)));

    [HttpPost("employees/{employeeId:long}/ledger")]
    [Authorize(Policy = "hr.leave_balance.adjust")]
    public Task<ActionResult<HrLeaveLedgerWriteResult>> PostLedgerEntry(
        long employeeId, PostLeaveLedgerRequest request, CancellationToken ct) =>
        Execute<HrLeaveLedgerWriteResult>(async () =>
            Ok(await sender.Send(new PostHrLeaveLedgerEntryCommand(
                employeeId, request.LeaveTypeId, request.TransactionType, request.Quantity,
                request.EffectiveDate, request.IdempotencyKey, request.Reason), ct)));

    [HttpPost("employees/{employeeId:long}/accruals")]
    [Authorize(Policy = "hr.leave_balance.accrue")]
    public Task<ActionResult<HrLeaveLedgerWriteResult>> Accrue(
        long employeeId, AccrueLeaveRequest request, CancellationToken ct) =>
        Execute<HrLeaveLedgerWriteResult>(async () =>
            Ok(await sender.Send(new AccrueHrLeaveCommand(
                employeeId, request.LeaveTypeId, request.ThroughDate), ct)));

    [HttpPost("employees/{employeeId:long}/leave-years/{leaveYearStart:datetime}/review-close")]
    [Authorize(Policy = "hr.leave_year.close")]
    public Task<ActionResult<HrLeaveYearLifecycleModel>> ReviewLeaveYearClose(
        long employeeId, DateOnly leaveYearStart, LeaveYearLifecycleRequest request, CancellationToken ct) =>
        Execute<HrLeaveYearLifecycleModel>(async () =>
            Ok(await sender.Send(new ReviewHrLeaveYearCloseCommand(
                employeeId, request.LeaveTypeId, leaveYearStart,
                request.ExpectedVersion, request.IdempotencyKey), ct)));

    [HttpPost("employees/{employeeId:long}/leave-years/{leaveYearStart:datetime}/close")]
    [Authorize(Policy = "hr.leave_year.close")]
    public Task<ActionResult<HrLeaveYearLifecycleModel>> CloseLeaveYear(
        long employeeId, DateOnly leaveYearStart, LeaveYearLifecycleRequest request, CancellationToken ct) =>
        Execute<HrLeaveYearLifecycleModel>(async () =>
            Ok(await sender.Send(new CloseHrLeaveYearCommand(
                employeeId, request.LeaveTypeId, leaveYearStart,
                request.ExpectedVersion, request.IdempotencyKey), ct)));

    [HttpPost("employees/{employeeId:long}/leave-years/{leaveYearStart:datetime}/reopen")]
    [Authorize(Policy = "hr.leave_year.reopen")]
    public Task<ActionResult<HrLeaveYearLifecycleModel>> ReopenLeaveYear(
        long employeeId, DateOnly leaveYearStart, ReopenLeaveYearRequest request, CancellationToken ct) =>
        Execute<HrLeaveYearLifecycleModel>(async () =>
            Ok(await sender.Send(new ReopenHrLeaveYearCommand(
                employeeId, request.LeaveTypeId, leaveYearStart,
                request.ExpectedVersion, request.IdempotencyKey, request.Reason), ct)));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<ActionResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("HR leave access rejected: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Leave access denied.", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid leave request.", detail: exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("HR leave operation conflicted: {Message}", exception.Message);
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Leave operation conflict.", detail: exception.Message);
        }
    }

    private void SetPrivateNoStoreHeaders()
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
    }
}

public sealed record CreateLeaveRequestBody(long LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string Reason);
public sealed record SubmitLeaveRequestBody(long ExpectedVersion);
public sealed record CancelLeaveRequestBody(string DecisionKey, string? Reason);
public sealed record DecideLeaveRequestBody(string DecisionKey, string? Reason);
public sealed class UploadLeaveAttachmentBody
{
    public IFormFile? File { get; init; }
}

public sealed record PostLeaveLedgerRequest(
    long LeaveTypeId,
    RetailPOS.Core.Entities.HR.Leave.HrLeaveLedgerTransactionType TransactionType,
    decimal Quantity,
    DateOnly EffectiveDate,
    string IdempotencyKey,
    string Reason);

public sealed record AccrueLeaveRequest(long LeaveTypeId, DateOnly ThroughDate);
public sealed record LeaveYearLifecycleRequest(
    long LeaveTypeId, long ExpectedVersion, string IdempotencyKey);
public sealed record ReopenLeaveYearRequest(
    long LeaveTypeId, long ExpectedVersion, string IdempotencyKey, string Reason);
