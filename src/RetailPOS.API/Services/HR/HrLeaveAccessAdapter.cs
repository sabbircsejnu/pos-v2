using System.Security.Claims;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Entities.HR.Leave;

namespace RetailPOS.API.Services.HR;

public sealed class HrLeaveAccessAdapter(
    IHttpContextAccessor httpContextAccessor,
    IHrOrganisationAccess organisationAccess,
    IHrPayrollCapabilityService capabilities,
    ITenantAccessService tenantAccess,
    IHrEmployeeLocationAssignmentStore employeeLocations,
    IHrEmployeeStore employees,
    IHrOrganisationReadStore organisationReadStore) : IHrLeaveAccess, IHrLeaveRequestAccess
{
    public long RequireBusinessId()
    {
        var businessId = organisationAccess.RequireBusinessId();
        if (tenantAccess.RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Leave access is outside the active Business.");
        return businessId;
    }

    public long RequireRealUserId() => organisationAccess.RequireRealUserId();

    public async Task<long> RequireCurrentEmployeeIdAsync(
        long businessId, CancellationToken cancellationToken)
    {
        var current = await employees.GetCurrentEmployeeAsync(
            businessId, RequireRealUserId(), cancellationToken);
        return current?.Id ?? throw new UnauthorizedAccessException(
            "The authenticated user is not linked to an employee in this Business.");
    }

    async Task IHrLeaveRequestAccess.EnsureAsync(
        long businessId,
        HrLeaveOperation operation,
        long? employeeId,
        long? locationId,
        CancellationToken cancellationToken)
    {
        if (businessId <= 0 || RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Leave request access is outside the active Business.");
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Attendance, cancellationToken);

        var permission = operation switch
        {
            HrLeaveOperation.CreateRequest or HrLeaveOperation.SubmitRequest
                or HrLeaveOperation.CancelRequest => "hr.leave.request",
            HrLeaveOperation.DecideRequest => "hr.leave.approve",
            HrLeaveOperation.ViewRequests => "hr.leave.view",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var principal = httpContextAccessor.HttpContext?.User;
        var accepted = PermissionCatalog.ExpandClaims(permission);
        var hasPermission = principal?.Identity?.IsAuthenticated == true
            && principal.Claims.Any(claim => claim.Type == "permission"
                && (claim.Value == "*" || accepted.Contains(claim.Value, StringComparer.OrdinalIgnoreCase)));
        var canReadToApprove = operation == HrLeaveOperation.ViewRequests
            && principal?.Claims.Any(claim => claim.Type == "permission"
                && (claim.Value == "*" || PermissionCatalog.ExpandClaims("hr.leave.approve")
                    .Contains(claim.Value, StringComparer.OrdinalIgnoreCase))) == true;
        if (!hasPermission && !canReadToApprove)
        {
            var currentEmployeeId = await RequireCurrentEmployeeIdAsync(businessId, cancellationToken);
            if (operation != HrLeaveOperation.ViewRequests || employeeId != currentEmployeeId)
                throw new UnauthorizedAccessException("The caller lacks the required leave request permission.");
        }

        var scope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
        if (employeeId is > 0)
        {
            var currentEmployeeId = await RequireCurrentEmployeeIdAsync(businessId, cancellationToken);
            var isSelf = currentEmployeeId == employeeId.Value;
            if (operation is (HrLeaveOperation.CreateRequest or HrLeaveOperation.SubmitRequest
                or HrLeaveOperation.CancelRequest) && !isSelf)
                throw new UnauthorizedAccessException("Employees may act only on their own leave requests.");
            if (!await employeeLocations.IsEmployeeAccessibleAsync(
                    businessId, employeeId.Value, scope.AllowedSources, cancellationToken))
                throw new UnauthorizedAccessException(
                    "Employee is outside the caller's Primary location scope.");
        }
        if (locationId is > 0)
            await organisationAccess.EnsureLocationAccessibleAsync(businessId, locationId.Value, cancellationToken);
    }

    async Task<bool> IHrLeaveRequestAccess.CanViewRequestAsync(
        long businessId, long employeeId, long locationId, CancellationToken cancellationToken)
    {
        if (businessId <= 0 || RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Leave request access is outside the active Business.");
        var scope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
        return await employeeLocations.IsEmployeeAccessibleAsync(
                businessId, employeeId, scope.AllowedSources, cancellationToken)
            && await organisationReadStore.IsLocationAccessibleAsync(
                businessId, locationId, scope, cancellationToken);
    }

    public async Task EnsureAsync(
        long businessId,
        HrLeaveOperation operation,
        HrLeavePolicyScopeType? scopeType,
        string? scopeReference,
        long? employeeId,
        CancellationToken cancellationToken)
    {
        if (businessId <= 0 || RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Leave access is outside the active Business.");
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);

        var permission = operation switch
        {
            HrLeaveOperation.ManageTypes or HrLeaveOperation.ManagePolicies => "hr.leave.admin",
            HrLeaveOperation.ViewTypes or HrLeaveOperation.ViewPolicies => "hr.leave.view",
            HrLeaveOperation.ViewBalances => "hr.leave_balance.view",
            HrLeaveOperation.AdjustBalances => "hr.leave_balance.adjust",
            HrLeaveOperation.AccrueBalances => "hr.leave_balance.accrue",
            HrLeaveOperation.ReviewLeaveYearClose or HrLeaveOperation.CloseLeaveYear => "hr.leave_year.close",
            HrLeaveOperation.ReopenLeaveYear => "hr.leave_year.reopen",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var principal = httpContextAccessor.HttpContext?.User;
        var accepted = PermissionCatalog.ExpandClaims(permission);
        var hasPermission = principal?.Identity?.IsAuthenticated == true
            && principal.Claims.Any(claim => claim.Type == "permission"
                && (claim.Value == "*" || accepted.Contains(claim.Value, StringComparer.OrdinalIgnoreCase)));
        var canReadForRequest = operation == HrLeaveOperation.ViewTypes
            && principal?.Claims.Any(claim => claim.Type == "permission"
                && (claim.Value == "*"
                    || PermissionCatalog.ExpandClaims("hr.leave.request")
                        .Contains(claim.Value, StringComparer.OrdinalIgnoreCase)
                    || PermissionCatalog.ExpandClaims("hr.leave.approve")
                        .Contains(claim.Value, StringComparer.OrdinalIgnoreCase)
                    || PermissionCatalog.ExpandClaims("hr.leave_year.close")
                        .Contains(claim.Value, StringComparer.OrdinalIgnoreCase)
                    || PermissionCatalog.ExpandClaims("hr.leave_year.reopen")
                        .Contains(claim.Value, StringComparer.OrdinalIgnoreCase))) == true;
        if (!hasPermission && !canReadForRequest)
            throw new UnauthorizedAccessException("The caller lacks the required leave permission.");

        var scope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
        if (employeeId is > 0)
        {
            if (!await employeeLocations.IsEmployeeAccessibleAsync(
                    businessId, employeeId.Value, scope.AllowedSources, cancellationToken))
                throw new UnauthorizedAccessException("Employee is outside the caller's Primary location scope.");
            return;
        }

        if (canReadForRequest)
            return;

        if (scopeType == HrLeavePolicyScopeType.Location)
        {
            if (!long.TryParse(scopeReference, out var locationId) || locationId <= 0)
                throw new ArgumentException("Leave policy location scope is invalid.", nameof(scopeReference));
            await organisationAccess.EnsureLocationAccessibleAsync(businessId, locationId, cancellationToken);
            return;
        }

        if (scopeType is HrLeavePolicyScopeType.Business or HrLeavePolicyScopeType.LocationType
            || operation is HrLeaveOperation.ManageTypes or HrLeaveOperation.ViewTypes
                or HrLeaveOperation.ViewPolicies)
        {
            if (scope.AllowedSources is not null)
                throw new UnauthorizedAccessException("Business-wide leave configuration requires Business-wide location access.");
        }
    }
}
