using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.API.Services.HR;

public sealed class PayrollRunAccessAdapter(
    IPayrollSalarySetupAccess payrollAccess,
    IHrOrganisationAccess organisationAccess) : IPayrollRunAccess
{
    public long RequireBusinessId() => payrollAccess.RequireBusinessId();

    public long RequireRealUserId() => payrollAccess.RequireRealUserId();

    public Task EnsurePermissionAsync(
        long businessId, string permission, CancellationToken cancellationToken) =>
        payrollAccess.EnsureBusinessPermissionAsync(businessId, permission, cancellationToken);

    public async Task EnsureScopeAsync(
        long businessId,
        string scopeType,
        long? scopeId,
        CancellationToken cancellationToken)
    {
        if (scopeType == PayrollRunScopeTypes.Business && scopeId is null)
        {
            var scope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
            if (scope.AllowedSources is not null)
                throw new UnauthorizedAccessException(
                    "Business-wide Payroll runs require Business-wide operational-location access.");
            return;
        }

        if (scopeType == PayrollRunScopeTypes.Location && scopeId is > 0)
        {
            await organisationAccess.EnsureLocationAccessibleAsync(
                businessId, scopeId.Value, cancellationToken);
            return;
        }

        throw new UnauthorizedAccessException("Payroll run scope is not accessible.");
    }
}
