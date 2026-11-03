using MediatR;
using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.Application.HR.Payroll;

public static class PayrollAdvancedReportLimits
{
    public const int MaximumPageSize = 200;
    public const int MaximumExportRows = 5000;
}

public sealed record PayrollAdvancedReportQuery(
    string Kind,
    DateOnly From,
    DateOnly To,
    long? EmployeeId = null,
    long? LocationId = null,
    long? RunId = null,
    long? CompareRunId = null,
    int Page = 1,
    int PageSize = 50) : IRequest<PayrollAdvancedReportPage>;

public sealed record PayrollAdvancedReportRow(
    long? EmployeeId,
    string? EmployeeCode,
    string? EmployeeName,
    long? PeriodId,
    string? PeriodCode,
    long? RunId,
    int? RunRevision,
    string? RunType,
    long? OriginalRunId,
    string Category,
    string? ComponentCode,
    string? ComponentName,
    decimal? Gross,
    decimal? Deductions,
    decimal? Net,
    string Status,
    long? LocationId,
    string? LocationName,
    DateOnly? EffectiveDate,
    DateOnly? EffectiveTo,
    long? SalaryVersionId,
    decimal? Opening,
    decimal? Recovered,
    decimal? Outstanding,
    string? Detail);

public sealed record PayrollAdvancedReportPage(
    string Kind,
    IReadOnlyList<PayrollAdvancedReportRow> Items,
    int Page,
    int PageSize,
    long TotalCount);

public interface IPayrollAdvancedReportStore
{
    Task<PayrollAdvancedReportPage> QueryAsync(
        long businessId,
        PayrollAdvancedReportQuery request,
        HrOperationalLocationReadScope locationScope,
        CancellationToken cancellationToken);
}

public sealed class PayrollAdvancedReportHandler(
    IPayrollSalarySetupAccess access,
    IHrOrganisationAccess organisationAccess,
    IHrOrganisationReadStore organisationReadStore,
    IPayrollAdvancedReportStore store)
    : IRequestHandler<PayrollAdvancedReportQuery, PayrollAdvancedReportPage>
{
    private static readonly HashSet<string> Kinds =
    [
        "PayrollRegister", "PayrollComponents", "PayrollVariance", "CorrectionOffCycle",
        "SalaryHistory", "LoanRecovery", "RunStatus", "PaymentOutstanding", "CostByLocation"
    ];

    public async Task<PayrollAdvancedReportPage> Handle(
        PayrollAdvancedReportQuery request,
        CancellationToken cancellationToken)
    {
        if (!Kinds.Contains(request.Kind)
            || request.From == default
            || request.To < request.From
            || request.To.DayNumber - request.From.DayNumber > 366
            || request.EmployeeId is <= 0
            || request.LocationId is <= 0
            || request.RunId is <= 0
            || request.CompareRunId is <= 0
            || request.Page < 1
            || request.PageSize is < 1 or > PayrollAdvancedReportLimits.MaximumPageSize
            || (long)(request.Page - 1) * request.PageSize > int.MaxValue
            || request.Kind == "PayrollVariance"
                && (request.RunId is null || request.CompareRunId is null
                    || request.RunId == request.CompareRunId)
            || request.Kind != "PayrollVariance" && request.CompareRunId is not null)
            throw new ArgumentException("Payroll report filters or paging values are invalid.");

        var businessId = access.RequireBusinessId();
        await access.EnsureBusinessPermissionAsync(businessId, "payroll.report.view", cancellationToken);
        await access.EnsureBusinessPermissionAsync(businessId, "payroll.salary.view", cancellationToken);

        var locationScope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
        if (request.LocationId is long locationId
            && !await organisationReadStore.IsLocationAccessibleAsync(
                businessId, locationId, locationScope, cancellationToken))
            throw new UnauthorizedAccessException("The Payroll report location is outside the caller's scope.");

        return await store.QueryAsync(businessId, request, locationScope, cancellationToken);
    }
}
