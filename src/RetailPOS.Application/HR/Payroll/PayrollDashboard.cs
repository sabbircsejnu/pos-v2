using MediatR;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed record GetPayrollDashboardQuery(
    long? PeriodId = null,
    long? RunId = null,
    long? LocationId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? RunType = null) : IRequest<PayrollDashboardSummary>;

public sealed record PayrollDashboardMetric(string Status, long Count, decimal Amount = 0m);
public sealed record PayrollDashboardMethodMetric(string Method, decimal SettledAmount);
public sealed record PayrollDashboardPostingMetric(string Category, string Status, long Count);

public sealed record PayrollDashboardPeriodSummary(
    long? Id, string? Code, string? Status, DateOnly? Start, DateOnly? End);

public sealed record PayrollDashboardRunSummary(
    long? Id,
    string? Status,
    string? RunType,
    long EmployeeCount,
    decimal Gross,
    decimal TotalDeductions,
    decimal NetPayable,
    decimal ApprovedAmount,
    IReadOnlyList<PayrollDashboardMetric> ProcessingStates,
    IReadOnlyList<PayrollDashboardMetric> RunTypes,
    long UnresolvedCorrectionLineageCount);

public sealed record PayrollDashboardPaymentSummary(
    decimal TotalPayable,
    decimal TotalPaid,
    decimal TotalOutstanding,
    IReadOnlyList<PayrollDashboardMetric> States,
    long HeldCount,
    long ProcessingCount,
    long FailedCount,
    long ReturnedCount,
    long CompletedCount,
    IReadOnlyList<PayrollDashboardMethodMetric> Methods);

public sealed record PayrollDashboardReconciliationSummary(
    bool Applies,
    long ReconciledCount,
    long PartiallyReconciledCount,
    long UnreconciledCount,
    long ExceptionCount);

public sealed record PayrollDashboardAccountingSummary(
    bool Enabled,
    IReadOnlyList<PayrollDashboardPostingMetric> ExpensePostings,
    IReadOnlyList<PayrollDashboardPostingMetric> SettlementPostings,
    long BlockerCount);

public sealed record PayrollDashboardFinalSettlementSummary(
    long ApprovedPositiveCount,
    decimal ApprovedPositiveAmount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    long NegativeReceivableCount,
    decimal NegativeReceivableAmount,
    long FinanciallyCompletedCount,
    long UnresolvedFinancialCount);

public sealed record PayrollDashboardBlockerSummary(
    long UnresolvedPaymentCount,
    long PartialPaymentCount,
    long ReconciliationPendingCount,
    long AccountingPendingOrFailedCount,
    long CorrectionPendingCount);

public sealed record PayrollDashboardSummary(
    PayrollDashboardPeriodSummary Period,
    PayrollDashboardRunSummary Run,
    PayrollDashboardPaymentSummary Payments,
    PayrollDashboardReconciliationSummary Reconciliation,
    PayrollDashboardAccountingSummary Accounting,
    PayrollDashboardFinalSettlementSummary FinalSettlements,
    PayrollDashboardBlockerSummary Blockers);

public interface IPayrollDashboardStore
{
    Task<PayrollDashboardSummary> GetAsync(
        long businessId,
        GetPayrollDashboardQuery request,
        HrOperationalLocationReadScope locationScope,
        CancellationToken cancellationToken);
}

public sealed class PayrollDashboardHandler(
    IPayrollSalarySetupAccess access,
    IHrOrganisationAccess organisationAccess,
    IHrOrganisationReadStore organisationReadStore,
    IPayrollDashboardStore store) : IRequestHandler<GetPayrollDashboardQuery, PayrollDashboardSummary>
{
    public async Task<PayrollDashboardSummary> Handle(
        GetPayrollDashboardQuery request,
        CancellationToken cancellationToken)
    {
        if (request.PeriodId is <= 0 || request.RunId is <= 0 || request.LocationId is <= 0
            || request.FromDate.HasValue != request.ToDate.HasValue
            || (request.FromDate is DateOnly from && request.ToDate is DateOnly to
                && (to < from || to.DayNumber - from.DayNumber > 366))
            || (request.RunType is not null && !PayrollRunTypes.IsValid(request.RunType)))
            throw new ArgumentException("Payroll dashboard filters are invalid.");

        var businessId = access.RequireBusinessId();
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.dashboard.view", cancellationToken);
        await access.EnsureBusinessPermissionAsync(
            businessId, "payroll.salary.view", cancellationToken);

        var locationScope = await organisationAccess.GetLocationReadScopeAsync(
            businessId, cancellationToken);
        if (request.LocationId is long locationId
            && !await organisationReadStore.IsLocationAccessibleAsync(
                businessId, locationId, locationScope, cancellationToken))
            throw new UnauthorizedAccessException("The Payroll dashboard location is outside the caller's scope.");

        return await store.GetAsync(businessId, request, locationScope, cancellationToken);
    }
}
