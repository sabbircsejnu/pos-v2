using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;

namespace RetailPOS.Application.HR.Payroll;

public sealed record PayrollRunCalculationResponse(
    long PayrollRunId,
    int CalculatedEmployeeCount,
    int ExceptionEmployeeCount,
    bool IsReplay = false);

public sealed record PayrollEmployeeResultSummary(
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    long? LocationId,
    long SalaryVersionId,
    DateOnly SalaryEffectiveFrom,
    string Status,
    string? ExceptionCode,
    decimal? GrossEarnings,
    decimal? TotalDeductions,
    decimal? NetPay,
    string? CalculationSnapshotHash);

public sealed record PayrollEmployeeResultLineResponse(
    long? SalaryComponentId,
    string ComponentCode,
    string ComponentName,
    string LineType,
    decimal Amount,
    int CalculationOrder,
    string SourceType,
    string? SourceId,
    long? SourceVersion,
    decimal? InputQuantity,
    decimal? InputRate,
    decimal? InputAmount,
    string? EvidenceCode);

public sealed record PayrollEmployeeResultDetailsResponse(
    PayrollEmployeeResultSummary Result,
    IReadOnlyList<PayrollEmployeeResultLineResponse> Lines);

public sealed record PayrollEmployeeResultPageResponse(
    long PayrollRunId,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<PayrollEmployeeResultSummary> Items);

public interface IPayrollRunCalculationStore
{
    Task<PayrollRunCalculationResponse> CalculateAsync(
        long businessId, long periodId, long runId, CancellationToken cancellationToken);

    Task<PayrollEmployeeResultPageResponse> GetPageAsync(
        long businessId, long periodId, long runId, int page, int pageSize,
        CancellationToken cancellationToken);

    Task<PayrollEmployeeResultDetailsResponse> GetAsync(
        long businessId, long periodId, long runId, long employeeId,
        CancellationToken cancellationToken);
}

public sealed record CalculatePayrollRunCommand(long PeriodId, long RunId)
    : IRequest<PayrollRunCalculationResponse>;
public sealed record GetPayrollEmployeeResultsQuery(long PeriodId, long RunId, int Page, int PageSize)
    : IRequest<PayrollEmployeeResultPageResponse>;
public sealed record GetPayrollEmployeeResultQuery(long PeriodId, long RunId, long EmployeeId)
    : IRequest<PayrollEmployeeResultDetailsResponse>;

public sealed class PayrollRunCalculationHandlers(
    IPayrollRunAccess access,
    IPayrollRunStore runs,
    IPayrollRunCalculationStore calculations,
    IRequiredAuditTransaction audit) :
    IRequestHandler<CalculatePayrollRunCommand, PayrollRunCalculationResponse>,
    IRequestHandler<GetPayrollEmployeeResultsQuery, PayrollEmployeeResultPageResponse>,
    IRequestHandler<GetPayrollEmployeeResultQuery, PayrollEmployeeResultDetailsResponse>
{
    public async Task<PayrollRunCalculationResponse> Handle(
        CalculatePayrollRunCommand command, CancellationToken cancellationToken)
    {
        ValidateIdentity(command.PeriodId, command.RunId);
        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.calculate", cancellationToken);
        var run = await runs.GetAsync(businessId, command.PeriodId, command.RunId, cancellationToken);
        await access.EnsureScopeAsync(businessId, run.ScopeType, run.ScopeId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.run.calculated", "Payroll",
                "Calculated frozen inputs for a Payroll run.", "PayrollRun",
                command.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => calculations.CalculateAsync(
                businessId, command.PeriodId, command.RunId, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollEmployeeResultPageResponse> Handle(
        GetPayrollEmployeeResultsQuery query, CancellationToken cancellationToken)
    {
        ValidateIdentity(query.PeriodId, query.RunId);
        if (query.Page < 1 || query.PageSize is < 1 or > 200)
            throw new ArgumentException("Page must be positive and page size must be between 1 and 200.");
        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.view", cancellationToken);
        await access.EnsurePermissionAsync(businessId, "payroll.salary.view", cancellationToken);
        var run = await runs.GetAsync(businessId, query.PeriodId, query.RunId, cancellationToken);
        await access.EnsureScopeAsync(businessId, run.ScopeType, run.ScopeId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.run.results_accessed", "Payroll",
                "Accessed confidential Payroll run results.", "PayrollRun",
                query.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => calculations.GetPageAsync(
                businessId, query.PeriodId, query.RunId, query.Page, query.PageSize, token),
            cancellationToken);
    }

    public async Task<PayrollEmployeeResultDetailsResponse> Handle(
        GetPayrollEmployeeResultQuery query, CancellationToken cancellationToken)
    {
        ValidateIdentity(query.PeriodId, query.RunId);
        if (query.EmployeeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(query.EmployeeId));
        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.view", cancellationToken);
        await access.EnsurePermissionAsync(businessId, "payroll.salary.view", cancellationToken);
        var run = await runs.GetAsync(businessId, query.PeriodId, query.RunId, cancellationToken);
        await access.EnsureScopeAsync(businessId, run.ScopeType, run.ScopeId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.run.result_accessed", "Payroll",
                "Accessed a confidential Payroll employee result.", "PayrollRunEmployeeResult",
                $"{query.RunId}:{query.EmployeeId}"),
            token => calculations.GetAsync(
                businessId, query.PeriodId, query.RunId, query.EmployeeId, token),
            cancellationToken);
    }

    private static void ValidateIdentity(long periodId, long runId)
    {
        if (periodId <= 0 || runId <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodId));
    }
}
