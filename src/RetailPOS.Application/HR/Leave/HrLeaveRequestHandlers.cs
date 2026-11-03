using System.Globalization;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Application.HR.Leave;

public sealed class CreateHrLeaveRequestDraftHandler(
    IHrLeaveRequestAccess access,
    IHrLeaveRequestStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<CreateHrLeaveRequestDraftCommand, HrLeaveRequestModel>
{
    public async Task<HrLeaveRequestModel> Handle(
        CreateHrLeaveRequestDraftCommand command, CancellationToken cancellationToken)
    {
        HrLeaveRequestRules.ValidateDraft(command.Data);
        var businessId = access.RequireBusinessId();
        var actorId = access.RequireRealUserId();
        var employeeId = await access.RequireCurrentEmployeeIdAsync(businessId, cancellationToken);
        await access.EnsureAsync(businessId, HrLeaveOperation.CreateRequest, employeeId, null, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "hr.leave_request.draft_created", "HR",
                "Created a leave request draft.", "HrEmployee", employeeId.ToString(CultureInfo.InvariantCulture)),
            async token =>
            {
                var created = await store.CreateDraftAsync(
                    businessId, employeeId, actorId, command.Data, token);
                await access.EnsureAsync(businessId, HrLeaveOperation.CreateRequest,
                    employeeId, created.ScopeLocationProjectionId, token);
                return created;
            },
            cancellationToken);
    }
}

public sealed class GetMyHrLeaveRequestsHandler(
    IHrLeaveRequestAccess access,
    IHrLeaveRequestStore store)
    : IRequestHandler<GetMyHrLeaveRequestsQuery, IReadOnlyList<HrLeaveRequestModel>>
{
    public async Task<IReadOnlyList<HrLeaveRequestModel>> Handle(
        GetMyHrLeaveRequestsQuery query, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        var employeeId = await access.RequireCurrentEmployeeIdAsync(businessId, cancellationToken);
        await access.EnsureAsync(businessId, HrLeaveOperation.ViewRequests,
            employeeId, null, cancellationToken);
        return await store.ListAsync(businessId, employeeId, null, cancellationToken);
    }
}

public sealed class GetHrLeaveRequestsHandler(
    IHrLeaveRequestAccess access,
    IHrLeaveRequestStore store)
    : IRequestHandler<GetHrLeaveRequestsQuery, IReadOnlyList<HrLeaveRequestModel>>
{
    public async Task<IReadOnlyList<HrLeaveRequestModel>> Handle(
        GetHrLeaveRequestsQuery query, CancellationToken cancellationToken)
    {
        if (query.EmployeeId is <= 0 || query.Status is not null
            and not ("Draft" or "PendingApproval" or "Approved" or "Rejected" or "Cancelled"))
            throw new ArgumentException("Leave request filters are invalid.");
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrLeaveOperation.ViewRequests,
            query.EmployeeId, null, cancellationToken);
        var requests = await store.ListAsync(businessId, query.EmployeeId, query.Status, cancellationToken);
        var visible = new List<HrLeaveRequestModel>(requests.Count);
        foreach (var request in requests)
        {
            if (await access.CanViewRequestAsync(businessId, request.EmployeeId,
                    request.ScopeLocationProjectionId, cancellationToken))
                visible.Add(request);
        }
        return visible;
    }
}

public sealed class GetHrLeaveRequestHandler(
    IHrLeaveRequestAccess access,
    IHrLeaveRequestStore store)
    : IRequestHandler<GetHrLeaveRequestQuery, HrLeaveRequestModel?>
{
    public async Task<HrLeaveRequestModel?> Handle(
        GetHrLeaveRequestQuery query, CancellationToken cancellationToken)
    {
        if (query.RequestId <= 0)
            throw new ArgumentOutOfRangeException(nameof(query.RequestId));
        var businessId = access.RequireBusinessId();
        var request = await store.GetAsync(businessId, query.RequestId, cancellationToken);
        if (request is null)
            return null;
        await access.EnsureAsync(businessId, HrLeaveOperation.ViewRequests,
            request.EmployeeId, request.ScopeLocationProjectionId, cancellationToken);
        return request;
    }
}

public sealed class GetHrLeaveRequestAttachmentsHandler(
    IHrLeaveRequestAccess access,
    IHrLeaveRequestStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<GetHrLeaveRequestAttachmentsQuery, IReadOnlyList<HrLeaveRequestAttachmentModel>>
{
    public async Task<IReadOnlyList<HrLeaveRequestAttachmentModel>> Handle(
        GetHrLeaveRequestAttachmentsQuery query, CancellationToken cancellationToken)
    {
        if (query.RequestId <= 0)
            throw new ArgumentOutOfRangeException(nameof(query.RequestId));
        var businessId = access.RequireBusinessId();
        var request = await store.GetAsync(businessId, query.RequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        await access.EnsureAsync(businessId, HrLeaveOperation.ViewRequests,
            request.EmployeeId, request.ScopeLocationProjectionId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId, "hr.leave_request.attachment_metadata_accessed", "HR",
                "Read leave-request attachment metadata.",
                "HrLeaveRequest", request.Id.ToString(CultureInfo.InvariantCulture)),
            token => store.GetAttachmentsAsync(businessId, request.Id, token),
            cancellationToken);
    }
}

public sealed class SubmitHrLeaveRequestHandler(
    IHrLeaveRequestAccess access,
    IHrLeaveRequestStore store,
    ISender sender)
    : IRequestHandler<SubmitHrLeaveRequestCommand, ApprovalDecisionResult>
{
    public async Task<ApprovalDecisionResult> Handle(
        SubmitHrLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        if (command.RequestId <= 0 || command.ExpectedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(command));
        var businessId = access.RequireBusinessId();
        var actorId = access.RequireRealUserId();
        var request = await store.GetAsync(businessId, command.RequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        if (request.RequestedByUserId != actorId)
            throw new UnauthorizedAccessException("Only the leave request's real requester may submit it.");
        await access.EnsureAsync(businessId, HrLeaveOperation.SubmitRequest,
            request.EmployeeId, request.ScopeLocationProjectionId, cancellationToken);
        if (request.Status != "Draft" || request.Version != command.ExpectedVersion)
            throw new InvalidOperationException("Leave request draft changed or is no longer submit-ready.");
        var snapshot = await store.FindForApprovalAsync(businessId, request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        if (string.IsNullOrWhiteSpace(snapshot.RequestHash))
            throw new InvalidOperationException("Leave request draft has no canonical request hash.");
        return await sender.Send(new SubmitApprovalCommand(
            ApprovalProcessType.Leave,
            HrLeaveApprovalConstants.TargetType,
            request.Id.ToString(CultureInfo.InvariantCulture),
            ApprovalScopeType.Location,
            request.ScopeLocationProjectionId.ToString(CultureInfo.InvariantCulture),
            command.ExpectedVersion,
            snapshot.RequestHash), cancellationToken);
    }
}

public sealed class CancelHrLeaveRequestHandler(
    IHrLeaveRequestAccess access,
    IHrLeaveRequestStore store,
    ISender sender,
    IRequiredAuditTransaction audit)
    : IRequestHandler<CancelHrLeaveRequestCommand, HrLeaveRequestModel?>
{
    public async Task<HrLeaveRequestModel?> Handle(
        CancelHrLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        if (command.RequestId <= 0 || string.IsNullOrWhiteSpace(command.DecisionKey)
            || command.DecisionKey.Length > 100
            || command.Reason?.Length > 1000)
            throw new ArgumentException("Leave cancellation identity, decision key or reason is invalid.");
        var businessId = access.RequireBusinessId();
        var actorId = access.RequireRealUserId();
        var request = await store.GetAsync(businessId, command.RequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        await access.EnsureAsync(businessId, HrLeaveOperation.CancelRequest,
            request.EmployeeId, request.ScopeLocationProjectionId, cancellationToken);
        if (request.Status == "Cancelled")
        {
            if (request.RequestedByUserId != actorId)
                throw new UnauthorizedAccessException("Only the real requester may cancel this leave request.");
            return request;
        }
        if (request.Status == "PendingApproval")
        {
            await sender.Send(new CancelApprovalCommand(
                request.ApprovalInstanceId ?? throw new InvalidOperationException(
                    "Pending leave request has no approval instance."),
                command.DecisionKey, command.Reason ?? "Requester cancelled the pending request."),
                cancellationToken);
        }
        else
        {
            if (request.RequestedByUserId != actorId)
                throw new UnauthorizedAccessException("Only the real requester may cancel this leave request.");
            await audit.ExecuteAsync(
                new RequiredAuditOperation(businessId, "hr.leave_request.cancelled", "HR",
                    "Cancelled a draft or reversed an approved leave request.",
                    "HrLeaveRequest", request.Id.ToString(CultureInfo.InvariantCulture)),
                async token =>
                {
                    await store.CancelAsync(businessId, request.Id, actorId, command.Reason, token);
                    return true;
                },
                cancellationToken);
        }
        return await store.GetAsync(businessId, request.Id, cancellationToken);
    }
}

public sealed class UploadHrLeaveRequestAttachmentHandler(
    IHrLeaveRequestAccess access,
    IHrLeaveRequestStore store,
    ConfidentialFileService? files = null)
    : IRequestHandler<UploadHrLeaveRequestAttachmentCommand, HrLeaveRequestAttachmentModel>
{
    public async Task<HrLeaveRequestAttachmentModel> Handle(
        UploadHrLeaveRequestAttachmentCommand command, CancellationToken cancellationToken)
    {
        if (command.RequestId <= 0 || command.Content is null || !command.Content.CanRead
            || string.IsNullOrWhiteSpace(command.FileName) || command.FileName.Length > 255
            || command.FileName.Any(char.IsControl) || command.FileName.Contains('/')
            || command.FileName.Contains('\\') || command.FileName.Contains(':')
            || command.FileName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("A readable file and safe attachment name are required.");
        if (files is null)
            throw new ConfidentialStorageUnavailableException();
        var businessId = access.RequireBusinessId();
        var actorId = access.RequireRealUserId();
        var employeeId = await access.RequireCurrentEmployeeIdAsync(businessId, cancellationToken);
        var request = await store.GetAsync(businessId, command.RequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        if (request.EmployeeId != employeeId || request.Status != "Draft")
            throw new UnauthorizedAccessException("Only the current employee can attach files to their own draft.");
        await access.EnsureAsync(businessId, HrLeaveOperation.CreateRequest,
            employeeId, request.ScopeLocationProjectionId, cancellationToken);
        var versions = await store.GetAttachmentsAsync(businessId, request.Id, cancellationToken);
        var version = versions.Count == 0 ? 1 : versions.Max(row => row.VersionNo) + 1;
        await files.StageAndCommitVersionAsync(new ConfidentialFileUpload(
            businessId, request.Id, version, ConfidentialFileClassification.LeaveRequestAttachment,
            command.FileName, command.Content, null, null, command.ContentType, actorId,
            ExpectedSubjectVersion: request.Version), cancellationToken);
        var created = (await store.GetAttachmentsAsync(businessId, request.Id, cancellationToken))
            .Single(row => row.VersionNo == version);
        return created;
    }
}

public sealed class DownloadHrLeaveRequestAttachmentHandler(
    IHrLeaveRequestAccess access,
    IHrLeaveRequestStore store,
    ConfidentialFileService? files = null)
    : IRequestHandler<DownloadHrLeaveRequestAttachmentQuery, AuthorizedConfidentialFile>
{
    public async Task<AuthorizedConfidentialFile> Handle(
        DownloadHrLeaveRequestAttachmentQuery query, CancellationToken cancellationToken)
    {
        if (query.RequestId <= 0 || query.VersionId == Guid.Empty)
            throw new ArgumentException("Leave request and attachment version are required.");
        if (files is null)
            throw new ConfidentialStorageUnavailableException();
        var businessId = access.RequireBusinessId();
        var request = await store.GetAsync(businessId, query.RequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Leave request was not found in this Business.");
        await access.EnsureAsync(businessId, HrLeaveOperation.ViewRequests,
            request.EmployeeId, request.ScopeLocationProjectionId, cancellationToken);
        if (!await store.IsCurrentCleanAttachmentAsync(
                businessId, request.Id, query.VersionId, cancellationToken))
            throw new UnauthorizedAccessException("Only the current clean leave attachment can be downloaded.");
        return await files.OpenAuthorizedReadAsync(
            businessId, query.VersionId, cancellationToken);
    }
}
