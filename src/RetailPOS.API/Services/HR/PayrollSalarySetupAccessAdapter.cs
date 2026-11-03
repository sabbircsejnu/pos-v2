using RetailPOS.Application.Authorization;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Audit;

namespace RetailPOS.API.Services.HR;

public sealed class PayrollSalarySetupAccessAdapter(
    IHttpContextAccessor httpContextAccessor,
    IAuditContext auditContext,
    IHrPayrollCapabilityService capabilities,
    IHrOrganisationAccess organisationAccess) : IPayrollSalarySetupAccess,
        IPayrollEmployeePaymentDestinationAccess
{
    public long RequireBusinessId()
    {
        if (!auditContext.IsInitialized || auditContext.BusinessId is not > 0)
            throw new BusinessContextRequiredException("A validated Business scope is required.");
        return auditContext.BusinessId.Value;
    }

    public long RequireRealUserId()
    {
        if (!auditContext.IsInitialized || auditContext.RealUserId is not > 0)
            throw new UnauthorizedAccessException("A real authenticated user is required.");
        return auditContext.RealUserId.Value;
    }

    public async Task EnsureAsync(
        long businessId, long locationProjectionId, string permission, CancellationToken cancellationToken)
    {
        await EnsureBusinessPermissionAsync(businessId, permission, cancellationToken);
        await organisationAccess.EnsureLocationAccessibleAsync(
            businessId, locationProjectionId, cancellationToken);
    }

    public async Task EnsureBusinessPermissionAsync(
        long businessId, string permission, CancellationToken cancellationToken)
    {
        if (businessId <= 0 || RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Payroll access is outside the active Business.");
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || !HasPermissionClaim(principal, permission))
            throw new UnauthorizedAccessException("The caller lacks the required Payroll permission.");
        _ = RequireRealUserId();
    }

    public bool HasBusinessPermission(string permission)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true && HasPermissionClaim(principal, permission);
    }

    // Delegates to the shared evaluator so the "*" wildcard and aliases behave exactly as in the policies.
    private static bool HasPermissionClaim(System.Security.Claims.ClaimsPrincipal principal, string permission) =>
        PermissionCatalog.IsGranted(
            principal.Claims
                .Where(claim => claim.Type is "permission" or "permissions" or "Permission")
                .Select(claim => claim.Value),
            permission);
}
