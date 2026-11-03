using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Application.HR.Employees;

public sealed class ProcessDueHrEmployeeLocationTransfersCommandHandler(
    IHrEmployeeLocationTransferActivationStore store,
    IRequiredAuditTransaction audit,
    IAuditContext auditContext)
    : IRequestHandler<ProcessDueHrEmployeeLocationTransfersCommand, int>
{
    public async Task<int> Handle(
        ProcessDueHrEmployeeLocationTransfersCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BusinessId <= 0 || request.BatchSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(request));

        var transferIds = await store.GetDueTransferIdsAsync(
            request.BusinessId, request.EffectiveThrough, request.BatchSize, cancellationToken);
        var changedCount = 0;
        foreach (var transferId in transferIds)
        {
            auditContext.Initialize(new AuditContextSnapshot(
                Guid.NewGuid(), null, null, null, null, null, request.BusinessId,
                AuditSource.SystemJob, null, "job:hr-employee-location-transfer-activation",
                null, null, null, false, null, null, null, null, "System", null));

            var result = await audit.ExecuteAsync(
                new RequiredAuditOperation(
                    request.BusinessId,
                    "hr.employee_location_transfer.processed",
                    "HR",
                    "Processed a due employee location transfer.",
                    "HrEmployeeLocationTransfer",
                    transferId.ToString("D")),
                token => store.ActivateDueTransferAsync(
                    request.BusinessId, transferId, request.EffectiveThrough, token),
                result => result.Changed,
                cancellationToken);
            if (result.Changed)
                changedCount++;
        }

        return changedCount;
    }
}

public sealed record GetHrEmployeeLocationHistoryQuery(long EmployeeId)
    : IRequest<IReadOnlyList<HrEmployeeLocationAssignmentReadModel>>;

public sealed class GetHrEmployeeLocationTransfersQueryHandler(
    IHrOrganisationAccess access,
    IHrEmployeeLocationAssignmentStore store)
    : IRequestHandler<GetHrEmployeeLocationTransfersQuery, IReadOnlyList<HrEmployeeLocationTransferReadModel>>
{
    public async Task<IReadOnlyList<HrEmployeeLocationTransferReadModel>> Handle(
        GetHrEmployeeLocationTransfersQuery request, CancellationToken cancellationToken)
    {
        if (request.EmployeeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.EmployeeId));
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ViewEmployeeLocationAssignments,
            null, cancellationToken);
        var scope = await access.GetLocationReadScopeAsync(businessId, cancellationToken);
        if (!await store.IsEmployeeAccessibleAsync(
                businessId, request.EmployeeId, scope.AllowedSources, cancellationToken))
            throw new UnauthorizedAccessException("Employee is outside the caller's Primary location scope.");
        return await store.GetTransfersAsync(
            businessId, request.EmployeeId, scope.AllowedSources, cancellationToken);
    }
}

public sealed class GetHrEmployeeLocationHistoryQueryHandler(
    IHrOrganisationAccess access,
    IHrEmployeeLocationAssignmentStore store)
    : IRequestHandler<GetHrEmployeeLocationHistoryQuery, IReadOnlyList<HrEmployeeLocationAssignmentReadModel>>
{
    public async Task<IReadOnlyList<HrEmployeeLocationAssignmentReadModel>> Handle(
        GetHrEmployeeLocationHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EmployeeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.EmployeeId));
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ViewEmployeeLocationAssignments,
            null, cancellationToken);
        var scope = await access.GetLocationReadScopeAsync(businessId, cancellationToken);
        if (!await store.IsEmployeeAccessibleAsync(
                businessId, request.EmployeeId, scope.AllowedSources, cancellationToken))
            throw new UnauthorizedAccessException("Employee is outside the caller's Primary location scope.");
        return await store.GetHistoryAsync(businessId, request.EmployeeId, scope.AllowedSources, cancellationToken);
    }
}

public sealed record AssignHrEmployeeLocationCommand(
    long EmployeeId,
    long ExpectedEmployeeVersion,
    HrEmployeeLocationAssignmentData Data) : IRequest<HrEmployeeLocationAssignmentReadModel?>;

public sealed class AssignHrEmployeeLocationCommandHandler(
    IHrOrganisationAccess access,
    IHrEmployeeLocationAssignmentStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<AssignHrEmployeeLocationCommand, HrEmployeeLocationAssignmentReadModel?>
{
    public async Task<HrEmployeeLocationAssignmentReadModel?> Handle(
        AssignHrEmployeeLocationCommand request,
        CancellationToken cancellationToken)
    {
        ValidateAssignment(request);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.ManageEmployeeLocationAssignments,
            request.Data.OperationalLocationProjectionId, cancellationToken);
        var existingPrimary = await store.GetEffectivePrimaryAsync(businessId, request.EmployeeId, cancellationToken);
        if (existingPrimary is not null)
            await access.EnsureLocationAccessibleAsync(
                businessId, existingPrimary.OperationalLocationProjectionId, cancellationToken);
        var actorId = access.RequireRealUserId();
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "hr.employee_location.assigned", "HR",
                "Assigned an employee to an operational location.", "HrEmployee", request.EmployeeId.ToString()),
            token => store.CreateAsync(businessId, actorId, request.EmployeeId,
                request.ExpectedEmployeeVersion, request.Data, token),
            result => result is not null,
            cancellationToken);
    }

    internal static void ValidateAssignment(AssignHrEmployeeLocationCommand request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Data);
        if (request.EmployeeId <= 0 || request.ExpectedEmployeeVersion <= 0
            || request.Data.OperationalLocationProjectionId <= 0)
            throw new ArgumentException("Employee, location and expected version must be positive.");
        if (request.Data.AssignmentType is not ("Primary" or "Additional" or "Temporary"))
            throw new ArgumentException("Assignment type must be Primary, Additional or Temporary.");
        if (request.Data.EffectiveTo < request.Data.EffectiveFrom)
            throw new ArgumentException("Assignment effective-to date cannot precede effective-from date.");
        if (request.Data.IsTemporary && !request.Data.EffectiveTo.HasValue)
            throw new ArgumentException("Temporary assignments require an effective-to date.");
        if (string.IsNullOrWhiteSpace(request.Data.Reason) || request.Data.Reason.Trim().Length > 500)
            throw new ArgumentException("Assignment reason is required and must not exceed 500 characters.");
        if (request.Data.EffectiveFrom > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("Initial assignments cannot be future-effective; use an approved transfer request.");
    }
}

public sealed record RequestHrEmployeeLocationTransferCommand(
    long EmployeeId,
    HrEmployeeLocationTransferData Data) : IRequest<HrEmployeeLocationTransferSubmissionResult>;

public sealed class RequestHrEmployeeLocationTransferCommandHandler(
    IHrOrganisationAccess access,
    IHrEmployeeLocationAssignmentStore store,
    ISender sender)
    : IRequestHandler<RequestHrEmployeeLocationTransferCommand, HrEmployeeLocationTransferSubmissionResult>
{
    public async Task<HrEmployeeLocationTransferSubmissionResult> Handle(
        RequestHrEmployeeLocationTransferCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Data);
        if (request.EmployeeId <= 0 || request.Data.OperationalLocationProjectionId <= 0
            || request.Data.ExpectedEmployeeVersion <= 0)
            throw new ArgumentException("Employee, location and expected version must be positive.");
        if (request.Data.EffectiveFrom < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("Transfer effective date cannot be in the past.");
        if (string.IsNullOrWhiteSpace(request.Data.Reason) || request.Data.Reason.Trim().Length > 500)
            throw new ArgumentException("Transfer reason is required and must not exceed 500 characters.");

        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOrganisationOperation.RequestEmployeeTransfer,
            request.Data.OperationalLocationProjectionId, cancellationToken);
        var primary = await store.GetEffectivePrimaryAsync(businessId, request.EmployeeId, cancellationToken)
            ?? throw new InvalidOperationException("An initial primary assignment is required before a transfer.");
        await access.EnsureAsync(businessId, HrOrganisationOperation.RequestEmployeeTransfer,
            primary.OperationalLocationProjectionId, cancellationToken);

        var payload = new HrEmployeeLocationTransferPayload(
            Guid.NewGuid(), request.EmployeeId, request.Data.OperationalLocationProjectionId,
            request.Data.EffectiveFrom, request.Data.ExpectedEmployeeVersion, request.Data.Reason.Trim());
        var serializedPayload = JsonSerializer.Serialize(payload);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serializedPayload)));
        var result = await sender.Send(new SubmitApprovalCommand(
            ApprovalProcessType.EmployeeTransfer,
            HrEmployeeLocationTransferConstants.TargetType,
            payload.TransferId.ToString("D"),
            ApprovalScopeType.Location,
            request.Data.OperationalLocationProjectionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            1,
            hash,
            serializedPayload), cancellationToken);
        return new HrEmployeeLocationTransferSubmissionResult(payload.TransferId, result.InstanceId, result.Status.ToString());
    }
}

public sealed record DecideHrEmployeeLocationTransferCommand(
    long EmployeeId,
    Guid TransferId,
    string DecisionKey,
    string Reason,
    bool Approve) : IRequest<ApprovalDecisionResult>;

public sealed class DecideHrEmployeeLocationTransferCommandHandler(
    IHrOrganisationAccess access,
    IHrEmployeeLocationAssignmentStore store,
    ISender sender)
    : IRequestHandler<DecideHrEmployeeLocationTransferCommand, ApprovalDecisionResult>
{
    public async Task<ApprovalDecisionResult> Handle(
        DecideHrEmployeeLocationTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        var transfer = await store.GetTransferAsync(businessId, request.EmployeeId, request.TransferId,
            cancellationToken)
            ?? throw new UnauthorizedAccessException("Transfer request was not found in the current Business.");
        if (transfer.Status != "PendingApproval")
            throw new InvalidOperationException("Only a pending transfer can receive an Approval Kernel decision.");
        await access.EnsureAsync(businessId, HrOrganisationOperation.ApproveEmployeeTransfer,
            transfer.TargetLocationProjectionId, cancellationToken);
        var instanceId = transfer.ApprovalInstanceId
            ?? throw new InvalidOperationException("Transfer request has no Approval Kernel instance.");
        return await sender.Send(new RecordApprovalDecisionCommand(
            instanceId, request.DecisionKey,
            request.Approve ? ApprovalDecisionType.Approve : ApprovalDecisionType.Reject,
            request.Reason), cancellationToken);
    }
}

public sealed record CancelHrEmployeeLocationTransferCommand(
    long EmployeeId,
    Guid TransferId,
    string DecisionKey,
    string Reason) : IRequest<ApprovalDecisionResult>;

public sealed class CancelHrEmployeeLocationTransferCommandHandler(
    IHrOrganisationAccess access,
    IHrEmployeeLocationAssignmentStore store,
    ISender sender,
    IRequiredAuditTransaction audit)
    : IRequestHandler<CancelHrEmployeeLocationTransferCommand, ApprovalDecisionResult>
{
    public async Task<ApprovalDecisionResult> Handle(
        CancelHrEmployeeLocationTransferCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EmployeeId <= 0 || request.TransferId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.DecisionKey)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("Transfer cancellation fields are invalid.");
        var businessId = access.RequireBusinessId();
        var transfer = await store.GetTransferAsync(businessId, request.EmployeeId, request.TransferId,
            cancellationToken)
            ?? throw new UnauthorizedAccessException("Transfer request was not found in the current Business.");
        var currentPrimary = await store.GetEffectivePrimaryAsync(businessId, request.EmployeeId, cancellationToken);
        if (currentPrimary is not null)
            await access.EnsureLocationAccessibleAsync(
                businessId, currentPrimary.OperationalLocationProjectionId, cancellationToken);
        await access.EnsureAsync(businessId, HrOrganisationOperation.CancelEmployeeTransfer,
            transfer.TargetLocationProjectionId, cancellationToken);
        var instanceId = transfer.ApprovalInstanceId
            ?? throw new InvalidOperationException("Transfer request has no Approval Kernel instance.");
        if (transfer.Status == "ApprovedPendingEffective")
        {
            var actorId = access.RequireRealUserId();
            var cancelled = await audit.ExecuteAsync(
                new RequiredAuditOperation(
                    businessId,
                    "hr.employee_location_transfer.cancelled",
                    "HR",
                    "Cancelled a scheduled employee location transfer.",
                    "HrEmployeeLocationTransfer",
                    transfer.Id.ToString("D")),
                token => store.CancelScheduledTransferAsync(
                    businessId, request.EmployeeId, request.TransferId, actorId,
                    request.Reason, token),
                result => result,
                cancellationToken);
            if (!cancelled)
                throw new InvalidOperationException("Scheduled transfer changed before cancellation.");
            return new ApprovalDecisionResult(
                instanceId, ApprovalInstanceStatus.Approved, null, false, "Cancelled");
        }
        return await sender.Send(new CancelApprovalCommand(instanceId, request.DecisionKey, request.Reason),
            cancellationToken);
    }
}
