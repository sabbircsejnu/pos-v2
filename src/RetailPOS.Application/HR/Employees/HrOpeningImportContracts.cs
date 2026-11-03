namespace RetailPOS.Application.HR.Employees;

using RetailPOS.Application.HR.Payroll;

/// <summary>Immutable row validation or execution evidence; confidential source fields are omitted.</summary>
public sealed record HrImportRowResult(int RowNo, string ExternalKey, string Status,
    IReadOnlyList<string> Errors, string? CreatedEntityType = null, long? CreatedEntityId = null);

/// <summary>Business-scoped opening import summary without private storage references.</summary>
public sealed record HrImportBatchResult(long Id, string ImportType, string TemplateVersion,
    string Status, bool IsDryRun, int TotalRows, int ValidRows, int InvalidRows,
    int Revision, long Version, DateTimeOffset CreatedAtUtc, DateTimeOffset? ExecutedAtUtc,
    IReadOnlyList<HrImportRowResult> Rows);

public sealed record HrImportFile(string StorageKey, string EncryptionKeyReference,
    string ContentHash, DateTimeOffset RetainUntilUtc);
public sealed record HrImportSource(long Id, string ImportType, string TemplateVersion,
    HrImportFile File, string Status, long CreatedBy);
public sealed record HrImportPolicy(int MaxRows, int RetentionDays);
public sealed record HrOpeningImportRow(int RowNo, string ExternalKey, string EmployeeCode,
    HrEmployeeCreateData? Employee, long? PositionId, long? OrgUnitId, long? LocationId,
    string AssignmentType, DateOnly EffectiveFrom, IReadOnlyList<string> Errors,
    PayrollSalaryOpeningImportData? Salary = null, PayrollLoanOpeningImportData? Loan = null);

public sealed record PayrollSalaryOpeningImportData(
    long SalaryStructureId,
    DateOnly EffectiveFrom,
    IReadOnlyList<PayrollSalarySetupComponentInput> Components,
    string Reason);

public sealed record PayrollLoanOpeningImportData(
    string LoanType,
    decimal PrincipalAmount,
    decimal OutstandingAmount,
    DateOnly DisbursedDate,
    CreatePayrollEmployeeLoanRequest Request);

public interface IHrOpeningImportParser
{
    string Template(string importType, string templateVersion);
    IReadOnlyList<HrOpeningImportRow> Parse(ReadOnlyMemory<byte> content,
        string importType, string templateVersion, int maxRows);
}

public interface IHrOpeningImportStore
{
    Task LockAsync(long businessId, string key, CancellationToken cancellationToken);
    Task<HrImportBatchResult?> FindByKeyAsync(long businessId, string key,
        string contentHash, string importType, string templateVersion, CancellationToken cancellationToken);
    Task<HrImportBatchResult> CreateAsync(long businessId, long actorId, string key,
        string importType, string templateVersion, HrImportFile file, CancellationToken cancellationToken);
    Task<HrImportSource> GetSourceAsync(long businessId, long id, CancellationToken cancellationToken);
    Task<HrImportBatchResult> GetAsync(long businessId, long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ValidateAsync(long businessId, string importType,
        HrOpeningImportRow row, CancellationToken cancellationToken);
    Task<long?> FindEmployeeIdAsync(long businessId, string employeeCode, CancellationToken cancellationToken);
    Task<HrImportBatchResult> RecordValidationAsync(long businessId, long id,
        IReadOnlyList<HrImportRowResult> rows, CancellationToken cancellationToken);
    Task<HrImportBatchResult> ExecuteAsync(long businessId, long actorId, long id,
        IReadOnlyList<HrOpeningImportRow> rows, CancellationToken cancellationToken);
    Task RecordFailureAsync(long businessId, long id, CancellationToken cancellationToken);
}

public interface IHrOpeningImportService
{
    Task<HrImportBatchResult> UploadAsync(string importType, string templateVersion,
        string idempotencyKey, string fileName, string contentType, Stream content, CancellationToken cancellationToken);
    Task<HrImportBatchResult> GetAsync(long id, CancellationToken cancellationToken);
    Task<HrImportBatchResult> ValidateAsync(long id, CancellationToken cancellationToken);
    Task<HrImportBatchResult> ExecuteAsync(long id, CancellationToken cancellationToken);
    Task<string> TemplateAsync(string importType, string templateVersion, CancellationToken cancellationToken);
}
