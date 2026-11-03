using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Services.HR;

public sealed class PayrollPaymentAccessAdapter(
    IPayrollSalarySetupAccess payrollAccess,
    IPayrollEmployeePaymentDestinationAccess paymentDestinationAccess,
    IHrOrganisationAccess organisationAccess,
    IHrExitAccess exitAccess) : IPayrollPaymentAccess
{
    public long RequireBusinessId() => payrollAccess.RequireBusinessId();

    public long RequireRealUserId() => payrollAccess.RequireRealUserId();

    public Task EnsureBusinessPermissionAsync(
        long businessId, string permission, CancellationToken cancellationToken) =>
        payrollAccess.EnsureBusinessPermissionAsync(businessId, permission, cancellationToken);

    public Task EnsureLocationAccessibleAsync(
        long businessId, long locationId, CancellationToken cancellationToken) =>
        organisationAccess.EnsureLocationAccessibleAsync(
            businessId, locationId, cancellationToken);

    public Task EnsureFinalSettlementOperationAsync(
        long businessId, HrExitOperation operation, HrExitScope scope,
        CancellationToken cancellationToken) =>
        exitAccess.EnsureAsync(
            businessId, operation, scope.EmployeeId, scope.PrimaryLocationProjectionId,
            cancellationToken);

    public bool HasBusinessPermission(string permission) =>
        paymentDestinationAccess.HasBusinessPermission(permission);
}
