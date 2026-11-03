using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;

namespace RetailPOS.Application.HR.Payroll;

public sealed record PayrollExportRun(
    long BusinessId,
    string BusinessName,
    long RunId,
    long PayrollPeriodId,
    int RunNo,
    string RunType,
    string RunReference,
    string? CorrectionReference,
    long? OriginalRunId,
    string? CorrectionType,
    string ScopeType,
    long? ScopeId,
    string Status,
    int Revision,
    string CurrencyCode,
    int DecimalPrecision,
    string PeriodCode,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? PayDate);

public sealed record PayrollExportComponentLine(
    long Id,
    string Code,
    string Name,
    string Type,
    decimal Amount,
    int Order);

public sealed record PayrollExportPayment(
    decimal PaidAmount,
    decimal OutstandingAmount,
    string Status,
    IReadOnlyList<string> ReconciliationStatuses);

public sealed record PayrollExportEmployee(
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    long? HistoricalLocationId,
    string? HistoricalLocationName,
    string? HistoricalLocationReference,
    string? HistoricalLocationType,
    string? HistoricalPositionName,
    string? HistoricalPositionCode,
    decimal GrossEarnings,
    decimal TotalDeductions,
    decimal NetPayable,
    IReadOnlyList<PayrollExportComponentLine> Lines,
    PayrollExportPayment? Payment);

public sealed record PayrollExportStatusCount(string Status, int Count);

public sealed record PayrollExportSource(
    PayrollExportRun Run,
    IReadOnlyList<PayrollExportEmployee> Employees,
    bool IncludesPaymentData,
    decimal GrossTotal,
    decimal TotalDeductions,
    decimal NetPayable,
    decimal? PaidTotal,
    decimal? OutstandingTotal,
    IReadOnlyList<PayrollExportStatusCount> PaymentStatuses,
    IReadOnlyList<PayrollExportStatusCount> ReconciliationStatuses)
{
    public string DateFormat { get; init; } = "yyyy-MM-dd";
}

public sealed record PayrollExportDownload(string FileName, string ContentType, Stream Content);

public interface IPayrollExportStore
{
    Task<PayrollExportRun?> FindRunAsync(
        long businessId, long runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayrollExportEmployee>> GetEmployeesAsync(
        long businessId,
        long runId,
        int revision,
        long? primaryLocationScopeId,
        bool includePaymentData,
        CancellationToken cancellationToken);
}

public interface IPayrollExportRenderer
{
    byte[] RenderCsv(PayrollExportSource source);
    byte[] RenderExcel(PayrollExportSource source);
    byte[] RenderPdfSummary(PayrollExportSource source);
}

public sealed record ExportPayrollRunCsvQuery(long RunId) : IRequest<PayrollExportDownload>;
public sealed record ExportPayrollRunExcelQuery(long RunId) : IRequest<PayrollExportDownload>;
public sealed record ExportPayrollRunPdfSummaryQuery(long RunId) : IRequest<PayrollExportDownload>;

public sealed class PayrollExportService(
    IPayrollRunAccess access,
    IPayrollEmployeePaymentDestinationAccess paymentAccess,
    IPayrollExportStore store,
    IPayrollExportRenderer renderer,
    IRequiredAuditTransaction audit,
    RetailPOS.Application.Settings.Abstractions.ISystemSettingsStore settings)
{
    private const string ExportPermission = "payroll.export";
    private const string ConfidentialViewPermission = "payroll.salary.view";
    private const string PaymentViewPermission = "payroll.payment.view";

    public Task<PayrollExportDownload> CsvAsync(
        long runId, CancellationToken cancellationToken) =>
        ExportAsync(runId, "CSV", renderer.RenderCsv, cancellationToken);

    public Task<PayrollExportDownload> ExcelAsync(
        long runId, CancellationToken cancellationToken) =>
        ExportAsync(runId, "Excel", renderer.RenderExcel, cancellationToken);

    public Task<PayrollExportDownload> PdfSummaryAsync(
        long runId, CancellationToken cancellationToken) =>
        ExportAsync(runId, "PDFSummary", renderer.RenderPdfSummary, cancellationToken);

    private async Task<PayrollExportDownload> ExportAsync(
        long runId,
        string exportType,
        Func<PayrollExportSource, byte[]> render,
        CancellationToken cancellationToken)
    {
        if (runId <= 0)
            throw new ArgumentOutOfRangeException(nameof(runId));

        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(businessId, ExportPermission, cancellationToken);
        await access.EnsurePermissionAsync(businessId, ConfidentialViewPermission, cancellationToken);

        var run = await store.FindRunAsync(businessId, runId, cancellationToken);
        if (run is null || !IsProtected(run.Status))
            throw new KeyNotFoundException("Approved Payroll run not found.");

        await access.EnsureScopeAsync(
            businessId, run.ScopeType, run.ScopeId, cancellationToken);

        var includesPaymentData = paymentAccess.HasBusinessPermission(PaymentViewPermission);
        var primaryLocationScopeId = run.ScopeType == Core.Entities.HR.Payroll.PayrollRunScopeTypes.Location
            ? run.ScopeId
            : null;
        var employees = await store.GetEmployeesAsync(
            businessId, runId, run.Revision, primaryLocationScopeId,
            includesPaymentData, cancellationToken);

        var company = (await settings.GetAsync(cancellationToken)).Company;
        var source = CreateSource(run, employees, includesPaymentData) with { DateFormat = company.DateFormat };
        byte[]? content = render(source);
        try
        {
            if (content.Length == 0)
                throw new InvalidDataException("Payroll export generation returned empty content.");

            var contentHash = Convert.ToHexString(SHA256.HashData(content));
            var metadata = JsonSerializer.Serialize(new
            {
                run.RunId,
                run.RunNo,
                run.RunType,
                run.Revision,
                ExportType = exportType,
                RowCount = employees.Count,
                ScopeType = run.ScopeType,
                ScopeId = run.ScopeId,
                IncludesPaymentData = includesPaymentData,
                OutputSha256 = contentHash
            });
            var actionType = $"payroll.export.{exportType.ToLowerInvariant()}";
            var entityId = run.RunId.ToString(CultureInfo.InvariantCulture);
            await audit.ExecuteAsync(
                new RequiredAuditOperation(
                    businessId, actionType, "Payroll",
                    $"Exported authoritative Payroll run data as {exportType}.",
                    "PayrollRun", entityId,
                    [
                        new AuditEventEntityWriteRequest(
                            "PayrollRun", entityId, "Exported", metadata)
                    ]),
                _ => Task.FromResult(true),
                cancellationToken);

            var download = new PayrollExportDownload(
                FileName(run, exportType),
                ContentType(exportType),
                new PayrollExportSensitiveStream(content));
            content = null;
            return download;
        }
        finally
        {
            if (content is not null)
                CryptographicOperations.ZeroMemory(content);
        }
    }

    private static PayrollExportSource CreateSource(
        PayrollExportRun run,
        IReadOnlyList<PayrollExportEmployee> employees,
        bool includesPaymentData)
    {
        var paymentStatuses = employees
            .Where(row => row.Payment is not null)
            .GroupBy(row => row.Payment!.Status, StringComparer.Ordinal)
            .Select(group => new PayrollExportStatusCount(group.Key, group.Count()))
            .OrderBy(row => row.Status, StringComparer.Ordinal)
            .ToArray();
        var reconciliationStatuses = employees
            .Where(row => row.Payment is not null)
            .SelectMany(row => row.Payment!.ReconciliationStatuses)
            .GroupBy(status => status, StringComparer.Ordinal)
            .Select(group => new PayrollExportStatusCount(group.Key, group.Count()))
            .OrderBy(row => row.Status, StringComparer.Ordinal)
            .ToArray();

        return new PayrollExportSource(
            run,
            employees,
            includesPaymentData,
            employees.Sum(row => row.GrossEarnings),
            employees.Sum(row => row.TotalDeductions),
            employees.Sum(row => row.NetPayable),
            includesPaymentData ? employees.Sum(row => row.Payment?.PaidAmount ?? 0m) : null,
            includesPaymentData ? employees.Sum(row => row.Payment?.OutstandingAmount ?? 0m) : null,
            paymentStatuses,
            reconciliationStatuses);
    }

    private static bool IsProtected(string status) =>
        status is Core.Entities.HR.Payroll.PayrollRunStatuses.Approved
            or Core.Entities.HR.Payroll.PayrollRunStatuses.PaymentInProgress
            or Core.Entities.HR.Payroll.PayrollRunStatuses.Paid
            or Core.Entities.HR.Payroll.PayrollRunStatuses.Completed;

    private static string FileName(PayrollExportRun run, string exportType)
    {
        var suffix = exportType switch
        {
            "CSV" => "register.csv",
            "Excel" => "register.xlsx",
            _ => "summary.pdf"
        };
        return $"payroll-run-{run.RunId.ToString(CultureInfo.InvariantCulture)}-r{run.Revision.ToString(CultureInfo.InvariantCulture)}-{suffix}";
    }

    private static string ContentType(string exportType) => exportType switch
    {
        "CSV" => "text/csv; charset=utf-8",
        "Excel" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/pdf"
    };
}

public sealed class PayrollExportSensitiveStream(byte[] content) : MemoryStream(content, writable: false)
{
    private readonly byte[] _content = content;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            CryptographicOperations.ZeroMemory(_content);
        base.Dispose(disposing);
    }
}

public sealed class PayrollExportHandlers(PayrollExportService service) :
    IRequestHandler<ExportPayrollRunCsvQuery, PayrollExportDownload>,
    IRequestHandler<ExportPayrollRunExcelQuery, PayrollExportDownload>,
    IRequestHandler<ExportPayrollRunPdfSummaryQuery, PayrollExportDownload>
{
    public Task<PayrollExportDownload> Handle(
        ExportPayrollRunCsvQuery query, CancellationToken cancellationToken) =>
        service.CsvAsync(query.RunId, cancellationToken);

    public Task<PayrollExportDownload> Handle(
        ExportPayrollRunExcelQuery query, CancellationToken cancellationToken) =>
        service.ExcelAsync(query.RunId, cancellationToken);

    public Task<PayrollExportDownload> Handle(
        ExportPayrollRunPdfSummaryQuery query, CancellationToken cancellationToken) =>
        service.PdfSummaryAsync(query.RunId, cancellationToken);
}
