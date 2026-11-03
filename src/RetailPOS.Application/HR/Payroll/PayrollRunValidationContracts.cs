using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;

namespace RetailPOS.Application.HR.Payroll;

public static class PayrollValidationSeverities
{
    public const string BlockingError = "BlockingError";
    public const string Warning = "Warning";
}

public sealed record PayrollRunValidationFinding(
    string Code,
    string Category,
    string Severity,
    long? EmployeeId,
    long? RelatedRunId,
    string Message);

public sealed record PayrollRunValidationResponse(
    long PayrollRunId,
    long PayrollPeriodId,
    string RunStatus,
    int CalculationVersion,
    bool IsCalculated,
    bool CanSubmit,
    int BlockingErrorCount,
    int WarningCount,
    int Page,
    int PageSize,
    int TotalFindings,
    IReadOnlyList<PayrollRunValidationFinding> Findings);

public interface IPayrollRunValidationStore
{
    Task<PayrollRunValidationResponse> ValidateAsync(
        long businessId,
        long periodId,
        PayrollRunResponse run,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

public sealed record GetPayrollRunValidationQuery(
    long PeriodId,
    long RunId,
    int Page,
    int PageSize) : IRequest<PayrollRunValidationResponse>;

public sealed class PayrollRunValidationHandler(
    IPayrollRunAccess access,
    IPayrollRunStore runs,
    IPayrollRunValidationStore validation,
    IRequiredAuditTransaction audit)
    : IRequestHandler<GetPayrollRunValidationQuery, PayrollRunValidationResponse>
{
    public async Task<PayrollRunValidationResponse> Handle(
        GetPayrollRunValidationQuery query,
        CancellationToken cancellationToken)
    {
        if (query.PeriodId <= 0 || query.RunId <= 0)
            throw new ArgumentOutOfRangeException(nameof(query.PeriodId));
        if (query.Page < 1 || query.PageSize is < 1 or > 200)
            throw new ArgumentException("Page must be positive and page size must be between 1 and 200.");

        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.view", cancellationToken);
        await access.EnsurePermissionAsync(businessId, "payroll.salary.view", cancellationToken);
        var run = await runs.GetAsync(businessId, query.PeriodId, query.RunId, cancellationToken);
        await access.EnsureScopeAsync(businessId, run.ScopeType, run.ScopeId, cancellationToken);

        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.run.validation_accessed", "Payroll",
                "Accessed Payroll run validation findings.", "PayrollRun",
                query.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => validation.ValidateAsync(
                businessId, query.PeriodId, run, query.Page, query.PageSize, token),
            cancellationToken);
    }
}
