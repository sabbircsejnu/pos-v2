using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Services.HR;

public sealed class PayrollPeriodAccessAdapter(IPayrollSalarySetupAccess payrollAccess)
    : IPayrollPeriodAccess
{
    public long RequireBusinessId() => payrollAccess.RequireBusinessId();

    public long RequireRealUserId() => payrollAccess.RequireRealUserId();

    public Task EnsureBusinessPermissionAsync(
        long businessId, string permission, CancellationToken cancellationToken) =>
        payrollAccess.EnsureBusinessPermissionAsync(businessId, permission, cancellationToken);
}
