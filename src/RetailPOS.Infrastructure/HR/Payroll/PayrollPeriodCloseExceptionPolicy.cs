using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class DenyPayrollPeriodCloseExceptionPolicy : IPayrollPeriodCloseExceptionPolicy
{
    public Task<PayrollPeriodCloseExceptionPolicySnapshot?> GetApprovedPolicyAsync(
        long businessId, CancellationToken cancellationToken) =>
        Task.FromResult<PayrollPeriodCloseExceptionPolicySnapshot?>(null);
}
