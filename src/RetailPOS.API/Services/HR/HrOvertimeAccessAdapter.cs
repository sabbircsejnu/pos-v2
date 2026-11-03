using System.Security.Claims;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Overtime;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Entities.HR.Overtime;

namespace RetailPOS.API.Services.HR;

public sealed class HrOvertimeAccessAdapter(
    IHttpContextAccessor httpContextAccessor,
    IHrOrganisationAccess organisationAccess,
    IHrOrganisationReadStore organisationReadStore,
    IHrPayrollCapabilityService capabilities,
    ITenantAccessService tenantAccess,
    IHrEmployeeLocationAssignmentStore employeeLocations) : IHrOvertimeAccess
{
    public long RequireBusinessId()
    {
        var businessId = organisationAccess.RequireBusinessId();
        if (tenantAccess.RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Overtime access is outside the active Business.");
        return businessId;
    }

    public long RequireRealUserId() => organisationAccess.RequireRealUserId();

    public async Task EnsureAsync(
        long businessId,
        HrOvertimeOperation operation,
        long? employeeId,
        long? locationId,
        CancellationToken cancellationToken)
    {
        if (businessId <= 0 || RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Overtime access is outside the active Business.");
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Attendance, cancellationToken);

        var required = operation switch
        {
            HrOvertimeOperation.View => "hr.overtime.view",
            HrOvertimeOperation.ManagePolicy => "hr.overtime_policy.manage",
            HrOvertimeOperation.Calculate => "hr.overtime.calculate",
            HrOvertimeOperation.Submit => "hr.overtime.submit",
            HrOvertimeOperation.Approve => "hr.overtime.approve",
            HrOvertimeOperation.Cancel => "hr.overtime.submit",
            HrOvertimeOperation.Override => "hr.overtime.override",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var principal = httpContextAccessor.HttpContext?.User;
        var accepted = PermissionCatalog.ExpandClaims(required);
        var hasPermission = principal?.Identity?.IsAuthenticated == true
            && principal.Claims.Any(claim => claim.Type == "permission"
                && (claim.Value == "*" || accepted.Contains(claim.Value, StringComparer.OrdinalIgnoreCase)));
        var mayViewForApproval = operation == HrOvertimeOperation.View
            && HasPermission(principal, "hr.overtime.approve");
        if (!hasPermission && !mayViewForApproval)
            throw new UnauthorizedAccessException("The caller lacks the required overtime permission.");

        var scope = await GetLocationReadScopeAsync(businessId, cancellationToken);
        if (employeeId is > 0 && !await employeeLocations.IsEmployeeAccessibleAsync(
                businessId, employeeId.Value, scope.AllowedSources, cancellationToken))
            throw new UnauthorizedAccessException(
                "Employee is outside the caller's Primary location scope.");
        if (locationId is > 0)
            await organisationAccess.EnsureLocationAccessibleAsync(
                businessId, locationId.Value, cancellationToken);
    }

    public async Task EnsurePolicyScopeAsync(
        long businessId,
        HrOvertimePolicyScopeType scopeType,
        string? scopeReference,
        CancellationToken cancellationToken)
    {
        switch (scopeType)
        {
            case HrOvertimePolicyScopeType.Business:
                return;
            case HrOvertimePolicyScopeType.Location:
                if (!long.TryParse(scopeReference, out var locationId) || locationId <= 0)
                    throw new ArgumentException("A positive location scope reference is required.");
                await EnsureAsync(businessId, HrOvertimeOperation.ManagePolicy, null,
                    locationId, cancellationToken);
                return;
            case HrOvertimePolicyScopeType.Employee:
                if (!long.TryParse(scopeReference, out var employeeId) || employeeId <= 0)
                    throw new ArgumentException("A positive employee scope reference is required.");
                await EnsureAsync(businessId, HrOvertimeOperation.ManagePolicy, employeeId,
                    null, cancellationToken);
                return;
            case HrOvertimePolicyScopeType.LocationType:
                if (string.IsNullOrWhiteSpace(scopeReference)
                    || !await HasLocationTypeAsync(businessId, scopeReference, cancellationToken))
                    throw new UnauthorizedAccessException(
                        "No accessible operational locations match the overtime policy scope.");
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(scopeType));
        }
    }

    public Task<RetailPOS.Application.HR.Organisation.HrOperationalLocationReadScope>
        GetLocationReadScopeAsync(long businessId, CancellationToken cancellationToken) =>
        organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);

    private async Task<bool> HasLocationTypeAsync(
        long businessId, string sourceType, CancellationToken cancellationToken)
    {
        var scope = await GetLocationReadScopeAsync(businessId, cancellationToken);
        var locations = await organisationReadStore.GetOperationalLocationsAsync(
            businessId, sourceType, null, scope, cancellationToken);
        return locations.Count > 0;
    }

    private static bool HasPermission(ClaimsPrincipal? principal, string permission)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return false;
        var accepted = PermissionCatalog.ExpandClaims(permission);
        return principal.Claims.Any(claim => claim.Type == "permission"
            && (claim.Value == "*" || accepted.Contains(claim.Value, StringComparer.OrdinalIgnoreCase)));
    }
}
