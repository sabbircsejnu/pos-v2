using System.Security.Claims;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.Identity.Permissions;

namespace RetailPOS.API.Services.HR;

public sealed class HrEmployeeAccessAdapter(
    IHrOrganisationAccess organisationAccess,
    IHttpContextAccessor httpContextAccessor,
    IHrEmployeeLocationAssignmentStore locationAssignments) : IHrEmployeeAccess
{
    public long RequireBusinessId() => organisationAccess.RequireBusinessId();

    public long RequireRealUserId() => organisationAccess.RequireRealUserId();

    public Task EnsureAsync(
        long businessId,
        HrEmployeeOperation operation,
        CancellationToken cancellationToken) =>
        organisationAccess.EnsureAsync(businessId, operation switch
        {
            HrEmployeeOperation.View => HrOrganisationOperation.ViewEmployees,
            HrEmployeeOperation.Create => HrOrganisationOperation.CreateEmployees,
            HrEmployeeOperation.Edit => HrOrganisationOperation.EditEmployees,
            HrEmployeeOperation.ViewLifecycle => HrOrganisationOperation.ViewEmployeeLifecycle,
            HrEmployeeOperation.ManageLifecycle => HrOrganisationOperation.ManageEmployeeLifecycle,
            HrEmployeeOperation.Rehire => HrOrganisationOperation.RehireEmployee,
            HrEmployeeOperation.ViewPositionHistory => HrOrganisationOperation.ViewEmployees,
            HrEmployeeOperation.ManagePositionAssignments =>
                HrOrganisationOperation.ManageEmployeePositionAssignments,
            HrEmployeeOperation.LinkUser => HrOrganisationOperation.LinkEmployeeUser,
            HrEmployeeOperation.UnlinkUser => HrOrganisationOperation.UnlinkEmployeeUser,
            HrEmployeeOperation.ResolveCurrentEmployee => HrOrganisationOperation.ResolveCurrentEmployee,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        }, null, cancellationToken);

    public async Task EnsureEmployeeLocationAccessAsync(
        long businessId,
        long employeeId,
        CancellationToken cancellationToken)
    {
        var scope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
        if (!await locationAssignments.IsEmployeeAccessibleAsync(
                businessId, employeeId, scope.AllowedSources, cancellationToken))
            throw new UnauthorizedAccessException("Employee is outside the caller's effective location scope.");
    }

    public async Task<IReadOnlyCollection<OperationalLocationSourceIdentity>?> GetEmployeeSearchLocationScopeAsync(
        long businessId,
        CancellationToken cancellationToken) =>
        (await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken)).AllowedSources;

    public HrEmployeeFieldAccess GetFieldAccess()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return new HrEmployeeFieldAccess(
            HasPermission(principal, "hr.employee.view_sensitive"),
            HasPermission(principal, "hr.employee.view_identity"));
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
