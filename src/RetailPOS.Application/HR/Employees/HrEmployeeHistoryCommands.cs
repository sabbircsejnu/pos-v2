using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities.HR.Employees;

namespace RetailPOS.Application.HR.Employees;

public sealed record GetHrEmployeeLifecycleHistoryQuery(long EmployeeId)
    : IRequest<IReadOnlyList<HrEmployeeLifecycleEventReadModel>>;

public sealed record GetHrEmployeePositionHistoryQuery(long EmployeeId)
    : IRequest<IReadOnlyList<HrEmployeePositionAssignmentReadModel>>;

public sealed record TransitionHrEmployeeLifecycleCommand(
    long EmployeeId,
    long ExpectedVersion,
    HrEmployeeLifecycleTransitionData Data)
    : IRequest<HrEmployeeLifecycleEventReadModel?>;

public sealed record RehireHrEmployeeCommand(
    long EmployeeId,
    long ExpectedVersion,
    HrEmployeeRehireData Data)
    : IRequest<HrEmployeeLifecycleEventReadModel?>;

public sealed record AssignHrEmployeePositionCommand(
    long EmployeeId,
    long ExpectedVersion,
    HrEmployeePositionAssignmentData Data)
    : IRequest<HrEmployeePositionAssignmentReadModel?>;

public sealed class GetHrEmployeeLifecycleHistoryQueryHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store)
    : IRequestHandler<GetHrEmployeeLifecycleHistoryQuery, IReadOnlyList<HrEmployeeLifecycleEventReadModel>>
{
    public async Task<IReadOnlyList<HrEmployeeLifecycleEventReadModel>> Handle(
        GetHrEmployeeLifecycleHistoryQuery request, CancellationToken cancellationToken)
    {
        ValidateEmployeeId(request.EmployeeId);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.View, cancellationToken);
        await access.EnsureAsync(businessId, HrEmployeeOperation.ViewLifecycle, cancellationToken);
        await access.EnsureEmployeeLocationAccessAsync(businessId, request.EmployeeId, cancellationToken);
        return await store.GetLifecycleHistoryAsync(businessId, request.EmployeeId, cancellationToken);
    }

    internal static void ValidateEmployeeId(long employeeId)
    {
        if (employeeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(employeeId));
    }
}

public sealed class GetHrEmployeePositionHistoryQueryHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store)
    : IRequestHandler<GetHrEmployeePositionHistoryQuery, IReadOnlyList<HrEmployeePositionAssignmentReadModel>>
{
    public async Task<IReadOnlyList<HrEmployeePositionAssignmentReadModel>> Handle(
        GetHrEmployeePositionHistoryQuery request, CancellationToken cancellationToken)
    {
        GetHrEmployeeLifecycleHistoryQueryHandler.ValidateEmployeeId(request.EmployeeId);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.View, cancellationToken);
        await access.EnsureEmployeeLocationAccessAsync(businessId, request.EmployeeId, cancellationToken);
        return await store.GetPositionHistoryAsync(businessId, request.EmployeeId, cancellationToken);
    }
}

public sealed class TransitionHrEmployeeLifecycleCommandHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<TransitionHrEmployeeLifecycleCommand, HrEmployeeLifecycleEventReadModel?>
{
    private static readonly HashSet<string> SupportedTransitions = new(StringComparer.Ordinal)
    {
        "Probation", "Confirmed", "EmploymentTypeChanged", "Suspended", "Reinstated"
    };

    public async Task<HrEmployeeLifecycleEventReadModel?> Handle(
        TransitionHrEmployeeLifecycleCommand request, CancellationToken cancellationToken)
    {
        Validate(request);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.View, cancellationToken);
        await access.EnsureAsync(businessId, HrEmployeeOperation.ManageLifecycle, cancellationToken);
        await access.EnsureEmployeeLocationAccessAsync(businessId, request.EmployeeId, cancellationToken);
        var actorUserId = access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "lifecycle_" + request.Data.EventType.ToLowerInvariant(),
                "hr", "Change employee lifecycle", "HrEmployee", request.EmployeeId.ToString()),
            token => store.TransitionLifecycleAsync(businessId, actorUserId, request.EmployeeId,
                request.ExpectedVersion, request.Data, token),
            result => result is not null,
            cancellationToken);
    }

    private static void Validate(TransitionHrEmployeeLifecycleCommand request)
    {
        GetHrEmployeeLifecycleHistoryQueryHandler.ValidateEmployeeId(request.EmployeeId);
        if (request.ExpectedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedVersion));
        ArgumentNullException.ThrowIfNull(request.Data);
        if (!SupportedTransitions.Contains(request.Data.EventType))
            throw new ArgumentException("Lifecycle event type is not a supported transition.");
        if (request.Data.EffectiveDate == default)
            throw new ArgumentException("An effective date is required.");
        if (request.Data.Reason?.Trim().Length > 1000)
            throw new ArgumentException("Lifecycle reason must not exceed 1000 characters.");
        if (request.Data.EventType == "EmploymentTypeChanged"
            ? request.Data.EmploymentTypeId is not > 0
            : request.Data.EmploymentTypeId.HasValue)
            throw new ArgumentException("Employment type is required only for EmploymentTypeChanged.");
    }
}

public sealed class RehireHrEmployeeCommandHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<RehireHrEmployeeCommand, HrEmployeeLifecycleEventReadModel?>
{
    public async Task<HrEmployeeLifecycleEventReadModel?> Handle(
        RehireHrEmployeeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.Data);
        GetHrEmployeeLifecycleHistoryQueryHandler.ValidateEmployeeId(request.EmployeeId);
        if (request.ExpectedVersion <= 0 || request.Data.EmploymentTypeId <= 0
            || request.Data.EffectiveDate == default)
            throw new ArgumentException("Employee version, employment type and rehire date are required.");
        if (request.Data.EmploymentStatus is not ("Active" or "Probation"))
            throw new ArgumentException("Rehire status must be Active or Probation.");
        if (request.Data.Reason?.Trim().Length > 1000)
            throw new ArgumentException("Rehire reason must not exceed 1000 characters.");

        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.View, cancellationToken);
        await access.EnsureAsync(businessId, HrEmployeeOperation.Rehire, cancellationToken);
        await access.EnsureEmployeeLocationAccessAsync(businessId, request.EmployeeId, cancellationToken);
        var actorUserId = access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "rehire", "hr", "Rehire employee",
                "HrEmployee", request.EmployeeId.ToString()),
            token => store.RehireAsync(businessId, actorUserId, request.EmployeeId,
                request.ExpectedVersion, request.Data, token),
            result => result is not null,
            cancellationToken);
    }
}

public sealed class AssignHrEmployeePositionCommandHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<AssignHrEmployeePositionCommand, HrEmployeePositionAssignmentReadModel?>
{
    public async Task<HrEmployeePositionAssignmentReadModel?> Handle(
        AssignHrEmployeePositionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.Data);
        GetHrEmployeeLifecycleHistoryQueryHandler.ValidateEmployeeId(request.EmployeeId);
        if (request.ExpectedVersion <= 0 || request.Data.PositionId <= 0 || request.Data.OrgUnitId <= 0
            || request.Data.EffectiveFrom == default
            || request.Data.EffectiveTo < request.Data.EffectiveFrom
            || !Enum.TryParse<HrEmployeeAssignmentType>(request.Data.AssignmentType, true, out var assignmentType)
            || !Enum.IsDefined(assignmentType)
            || request.Data.Reason?.Trim().Length > 500)
            throw new ArgumentException("Position assignment data is invalid.");

        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.View, cancellationToken);
        await access.EnsureEmployeeLocationAccessAsync(businessId, request.EmployeeId, cancellationToken);
        await access.EnsureAsync(businessId, HrEmployeeOperation.ManagePositionAssignments, cancellationToken);
        var actorUserId = access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "position_assignment", "hr",
                "Change employee position or manager", "HrEmployee", request.EmployeeId.ToString()),
            token => store.AssignPositionAsync(businessId, actorUserId, request.EmployeeId,
                request.ExpectedVersion, request.Data, token),
            result => result is not null,
            cancellationToken);
    }
}
