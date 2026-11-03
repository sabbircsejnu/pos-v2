using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;

namespace RetailPOS.Application.HR.Employees;

public sealed record GetHrEmployeeUserLinkQuery(long EmployeeId)
    : IRequest<HrEmployeeUserLinkState>;
public sealed record GetCurrentHrEmployeeQuery : IRequest<HrEmployeeSelfReadModel?>;
public sealed record LinkHrEmployeeUserCommand(long EmployeeId, long IdentityUserId, string Reason)
    : IRequest<HrEmployeeUserLinkReadModel?>;
public sealed record UnlinkHrEmployeeUserCommand(
    long EmployeeId, long ExpectedVersion, string Reason) : IRequest<HrEmployeeUserLinkReadModel?>;

public sealed class GetHrEmployeeUserLinkQueryHandler(IHrEmployeeAccess access, IHrEmployeeStore store)
    : IRequestHandler<GetHrEmployeeUserLinkQuery, HrEmployeeUserLinkState>
{
    public async Task<HrEmployeeUserLinkState> Handle(
        GetHrEmployeeUserLinkQuery request, CancellationToken cancellationToken)
    {
        ValidateEmployeeId(request.EmployeeId);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.View, cancellationToken);
        await access.EnsureEmployeeLocationAccessAsync(businessId, request.EmployeeId, cancellationToken);
        return new HrEmployeeUserLinkState(await store.GetActiveUserLinkAsync(
            businessId, request.EmployeeId, cancellationToken));
    }

    internal static void ValidateEmployeeId(long employeeId)
    {
        if (employeeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(employeeId));
    }
}

public sealed class GetCurrentHrEmployeeQueryHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store,
    IIdentityUserDirectory identityUsers)
    : IRequestHandler<GetCurrentHrEmployeeQuery, HrEmployeeSelfReadModel?>
{
    public async Task<HrEmployeeSelfReadModel?> Handle(
        GetCurrentHrEmployeeQuery request, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.ResolveCurrentEmployee, cancellationToken);
        var identityUserId = access.RequireRealUserId();
        var user = await identityUsers.FindAsync(identityUserId, cancellationToken);
        if (user is not { IsActive: true } || user.BusinessId != businessId)
            throw new UnauthorizedAccessException("The authenticated Login User is outside the active Business.");
        return await store.GetCurrentEmployeeAsync(businessId, identityUserId, cancellationToken);
    }
}

public sealed class LinkHrEmployeeUserCommandHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store,
    IIdentityUserDirectory identityUsers,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<LinkHrEmployeeUserCommand, HrEmployeeUserLinkReadModel?>
{
    public async Task<HrEmployeeUserLinkReadModel?> Handle(
        LinkHrEmployeeUserCommand request, CancellationToken cancellationToken)
    {
        GetHrEmployeeUserLinkQueryHandler.ValidateEmployeeId(request.EmployeeId);
        if (request.IdentityUserId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.IdentityUserId));
        ValidateReason(request.Reason, "Link reason");
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.LinkUser, cancellationToken);
        await access.EnsureEmployeeLocationAccessAsync(businessId, request.EmployeeId, cancellationToken);
        var actorUserId = access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "employee_user_linked", "hr",
                "Link Login User to employee", "HrEmployee", request.EmployeeId.ToString(),
                [new AuditEventEntityWriteRequest(
                    "HrEmployeeUserLink", request.EmployeeId.ToString(), "linked",
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        identityUserId = request.IdentityUserId,
                        reason = request.Reason.Trim()
                    }))]),
            async token =>
            {
                var user = await identityUsers.FindAsync(request.IdentityUserId, token);
                if (user is not { IsActive: true } || user.BusinessId != businessId)
                    throw new InvalidOperationException(
                        "The Login User was not found, is inactive, or is not assigned to this Business.");
                return await store.LinkUserAsync(businessId, actorUserId, request.EmployeeId,
                    request.IdentityUserId, request.Reason.Trim(), token);
            },
            result => result is not null,
            cancellationToken);
    }

    internal static void ValidateReason(string reason, string label)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException($"{label} is required and must not exceed 500 characters.");
    }
}

public sealed class UnlinkHrEmployeeUserCommandHandler(
    IHrEmployeeAccess access,
    IHrEmployeeStore store,
    IRequiredAuditTransaction auditTransaction)
    : IRequestHandler<UnlinkHrEmployeeUserCommand, HrEmployeeUserLinkReadModel?>
{
    public async Task<HrEmployeeUserLinkReadModel?> Handle(
        UnlinkHrEmployeeUserCommand request, CancellationToken cancellationToken)
    {
        GetHrEmployeeUserLinkQueryHandler.ValidateEmployeeId(request.EmployeeId);
        if (request.ExpectedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedVersion));
        LinkHrEmployeeUserCommandHandler.ValidateReason(request.Reason, "Unlink reason");
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrEmployeeOperation.UnlinkUser, cancellationToken);
        await access.EnsureEmployeeLocationAccessAsync(businessId, request.EmployeeId, cancellationToken);
        var actorUserId = access.RequireRealUserId();
        return await auditTransaction.ExecuteAsync(
            new RequiredAuditOperation(businessId, "employee_user_unlinked", "hr",
                "Unlink Login User from employee", "HrEmployee", request.EmployeeId.ToString()),
            token => store.UnlinkUserAsync(businessId, actorUserId, request.EmployeeId,
                request.ExpectedVersion, request.Reason.Trim(), token),
            result => result is not null,
            cancellationToken);
    }
}
