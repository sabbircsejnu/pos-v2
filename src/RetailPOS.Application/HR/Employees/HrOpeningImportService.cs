using System.Security.Cryptography;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.Application.HR.Employees;

public sealed class HrOpeningImportService(
    IHrOrganisationAccess access,
    IHrEmployeeAccess employeeAccess,
    IHrOpeningImportStore store,
    IHrOpeningImportParser parser,
    IRequiredAuditTransaction audit,
    TimeProvider clock,
    IPrivateBlobStore? blobs = null,
    IConfidentialFileEncryption? encryption = null,
    IConfidentialContentInspector? inspector = null,
    ConfidentialFileLimits? limits = null,
    HrImportPolicy? policy = null,
    IPayrollSalarySetupAccess? payrollAccess = null,
    IPayrollSalarySetupStore? salaries = null) : IHrOpeningImportService
{
    public async Task<string> TemplateAsync(string importType, string templateVersion, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken, false, importType);
        return parser.Template(importType, templateVersion);
    }

    public async Task<HrImportBatchResult> GetAsync(long id, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        access.RequireRealUserId();
        var source = await store.GetSourceAsync(businessId, id, cancellationToken);
        await RequireAsync(cancellationToken, false, source.ImportType);
        await EnsureBatchAccessAsync(businessId, source, cancellationToken);
        return await store.GetAsync(businessId, id, cancellationToken);
    }

    public async Task<HrImportBatchResult> UploadAsync(string importType, string templateVersion,
        string idempotencyKey, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var businessId = await RequireAsync(cancellationToken, true, importType);
        RequireStorage();
        parser.Template(importType, templateVersion);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100
            || idempotencyKey.Any(char.IsControl))
            throw new ArgumentException("A stable idempotency key of at most 100 characters is required.");
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255
            || fileName.Any(char.IsControl) || fileName.IndexOfAny(['/', '\\', ':']) >= 0
            || fileName.Contains("..", StringComparison.Ordinal)
            || !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(contentType.Split(';')[0].Trim(), "text/csv", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A safe CSV filename and text/csv MIME type are required.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        string? stagedKey = null;
        try
        {
            int count;
            while ((count = await content.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + count > limits!.MaxBytes)
                    throw new ArgumentException("Import exceeds the configured private file size limit.");
                await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
            }
            var bytes = buffer.ToArray();
            try
            {
                var rows = parser.Parse(bytes, importType, templateVersion, policy!.MaxRows);
                foreach (var row in rows)
                {
                    await AuthorizeRowAsync(businessId, importType, row, cancellationToken);
                    await EnsurePayrollRowAccessAsync(businessId, importType, row, true, cancellationToken);
                }
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                return await audit.ExecuteAsync(Operation(businessId, "uploaded", importType),
                    async token =>
                    {
                        await store.LockAsync(businessId, idempotencyKey, token);
                        var previous = await store.FindByKeyAsync(businessId, idempotencyKey, hash,
                            importType, templateVersion, token);
                        if (previous is not null)
                        {
                            await EnsureBatchAccessAsync(businessId,
                                await store.GetSourceAsync(businessId, previous.Id, token), token);
                            return previous;
                        }
                        var validation = await inspector!.ValidateAsync(bytes, fileName, token);
                        if (validation.ContentType != "text/csv" || validation.Extension != ".csv")
                            throw new ArgumentException("Actual import content does not match CSV.");
                        ConfidentialFileScanStatus scan;
                        try { scan = await inspector.ScanAsync(bytes, token); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception exception)
                        {
                            throw new ConfidentialFileScanException(ConfidentialFileScanStatus.Error,
                                "Import scan failed; acceptance denied.", exception);
                        }
                        if (scan != ConfidentialFileScanStatus.Clean)
                            throw new ConfidentialFileScanException(scan, "Import scan is not clean; acceptance denied.");
                        var encrypted = await encryption!.EncryptAsync(bytes, token);
                        try
                        {
                            var maxCiphertext = limits!.MaxCiphertextBytes > 0
                                ? limits.MaxCiphertextBytes : limits.MaxBytes;
                            if (encrypted.Bytes.Length == 0 || encrypted.Bytes.LongLength > maxCiphertext
                                || string.IsNullOrWhiteSpace(encrypted.KeyReference))
                                throw new InvalidOperationException("Import encryption requires a key-bound payload.");
                            var key = await blobs!.StageAsync(businessId, encrypted.Bytes, token);
                            stagedKey = key;
                            return await store.CreateAsync(businessId, access.RequireRealUserId(), idempotencyKey,
                                importType, templateVersion, new HrImportFile(key, encrypted.KeyReference, hash,
                                    clock.GetUtcNow().AddDays(policy.RetentionDays)), token);
                        }
                        finally { CryptographicOperations.ZeroMemory(encrypted.Bytes); }
                    }, cancellationToken);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception commitFailure) when (stagedKey is not null)
        {
            try { await blobs!.DeleteOwnedStageAsync(businessId, stagedKey, CancellationToken.None); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Import commit failed and its provider-owned stage could not be cleaned up.",
                    commitFailure, cleanupFailure);
            }
            throw;
        }
        catch (ConfidentialFileScanException)
        {
            await audit.ExecuteAsync(Operation(businessId, "rejected", importType),
                _ => Task.FromResult(true), cancellationToken);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chunk);
            CryptographicOperations.ZeroMemory(buffer.GetBuffer());
        }
    }

    public Task<HrImportBatchResult> ValidateAsync(long id, CancellationToken cancellationToken) =>
        ProcessAsync(id, false, cancellationToken);

    public Task<HrImportBatchResult> ExecuteAsync(long id, CancellationToken cancellationToken) =>
        ProcessAsync(id, true, cancellationToken);

    private async Task<HrImportBatchResult> ProcessAsync(long id, bool execute, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        access.RequireRealUserId();
        if (id <= 0) throw new ArgumentException("Import batch ID must be positive.");
        RequireStorage();
        var initialSource = await store.GetSourceAsync(businessId, id, cancellationToken);
        await RequireAsync(cancellationToken, true, initialSource.ImportType);
        try
        {
            return await audit.ExecuteAsync(Operation(
                businessId, execute ? "executed" : "dry_run", initialSource.ImportType),
                async token =>
                {
                    await store.LockAsync(businessId, id.ToString(), token);
                    var source = await store.GetSourceAsync(businessId, id, token);
                    await EnsureBatchAccessAsync(businessId, source, token);
                    if (source.Status == "Completed")
                    {
                        if (source.ImportType is "Salary" or "LoanOpening")
                        {
                            var completedRows = await ReadAsync(source, token);
                            foreach (var row in completedRows)
                                await EnsurePayrollRowAccessAsync(
                                    businessId, source.ImportType, row, true, token);
                        }
                        else if (source.ImportType == "Employee")
                            await employeeAccess.EnsureAsync(businessId, HrEmployeeOperation.Create, token);
                        else
                            await access.EnsureAsync(businessId,
                                HrOrganisationOperation.ManageEmployeeLocationAssignments, null, token);
                        return await store.GetAsync(businessId, id, token);
                    }
                    if (execute && source.Status != "Ready")
                        throw new InvalidOperationException("A successful dry-run is required before execution.");
                    var rows = await ReadAsync(source, token);
                    var validation = new List<HrImportRowResult>();
                    foreach (var row in rows)
                    {
                        await AuthorizeRowAsync(businessId, source.ImportType, row, token);
                        var errors = row.Errors.Concat(await store.ValidateAsync(businessId, source.ImportType, row, token)).ToArray();
                        await EnsurePayrollRowAccessAsync(
                            businessId, source.ImportType, row, true, token);
                        validation.Add(new(row.RowNo, row.ExternalKey, errors.Length == 0 ? "Valid" : "Invalid", errors));
                    }
                    if (!execute || validation.Any(row => row.Errors.Count > 0))
                        return await store.RecordValidationAsync(businessId, id, validation, token);
                    return await store.ExecuteAsync(businessId, access.RequireRealUserId(), id, rows, token);
                }, cancellationToken);
        }
        catch (HrImportExecutionException)
        {
            await audit.ExecuteAsync(Operation(businessId, "failed", initialSource.ImportType),
                async token =>
                {
                    await store.LockAsync(businessId, id.ToString(), token);
                    await store.RecordFailureAsync(businessId, id, token);
                    return true;
                }, cancellationToken);
            throw;
        }
    }

    private async Task<IReadOnlyList<HrOpeningImportRow>> ReadAsync(HrImportSource source, CancellationToken token)
    {
        if (source.File.RetainUntilUtc <= clock.GetUtcNow())
            throw new InvalidOperationException("The retained import source has expired; upload a new batch.");
        await using var stream = await blobs!.OpenAsync(access.RequireBusinessId(), source.File.StorageKey, token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        try
        {
            int count;
            while ((count = await stream.ReadAsync(chunk, token)) > 0)
            {
                var max = limits!.MaxCiphertextBytes > 0 ? limits.MaxCiphertextBytes : limits.MaxBytes;
                if (buffer.Length + count > max) throw new InvalidOperationException("Stored import exceeds approved limits.");
                await buffer.WriteAsync(chunk.AsMemory(0, count), token);
            }
            var ciphertext = buffer.ToArray();
            byte[] bytes;
            try { bytes = await encryption!.DecryptAsync(ciphertext, source.File.EncryptionKeyReference, token); }
            finally { CryptographicOperations.ZeroMemory(ciphertext); }
            try
            {
                if (bytes.LongLength > limits!.MaxBytes
                    || Convert.ToHexString(SHA256.HashData(bytes)) != source.File.ContentHash)
                    throw new InvalidOperationException("Retained import integrity check failed.");
                return parser.Parse(bytes, source.ImportType, source.TemplateVersion, policy!.MaxRows);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chunk);
            CryptographicOperations.ZeroMemory(buffer.GetBuffer());
        }
    }

    private async Task EnsureBatchAccessAsync(long businessId, HrImportSource source, CancellationToken token)
    {
        IReadOnlyList<HrOpeningImportRow>? payrollRows = null;
        if (source.ImportType is "Salary" or "LoanOpening")
        {
            RequireStorage();
            payrollRows = await ReadAsync(source, token);
            foreach (var row in payrollRows)
                await EnsurePayrollRowAccessAsync(businessId, source.ImportType, row, false, token);
        }
        if ((await access.GetLocationReadScopeAsync(businessId, token)).AllowedSources is null) return;
        if (source.CreatedBy != access.RequireRealUserId())
            throw new UnauthorizedAccessException("A location-scoped caller can review only their own import batches.");
        RequireStorage();
        foreach (var row in payrollRows ?? await ReadAsync(source, token))
        {
            await EnsurePayrollRowAccessAsync(businessId, source.ImportType, row, false, token);
            var employeeId = await store.FindEmployeeIdAsync(businessId, row.EmployeeCode, token);
            if (employeeId.HasValue)
                await employeeAccess.EnsureEmployeeLocationAccessAsync(businessId, employeeId.Value, token);
            else if (row.LocationId.HasValue && row.AssignmentType == "Primary")
                await access.EnsureLocationAccessibleAsync(businessId, row.LocationId.Value, token);
            else
                throw new UnauthorizedAccessException("Unassigned opening evidence requires Business-wide location access.");
        }
    }

    private async Task<long> RequireAsync(
        CancellationToken token, bool write = true, string? importType = null)
    {
        var businessId = access.RequireBusinessId();
        access.RequireRealUserId();
        if (importType is "Salary" or "LoanOpening")
        {
            if (payrollAccess is null)
                throw new HrImportStorageUnavailableException();
            await payrollAccess.EnsureBusinessPermissionAsync(businessId,
                write ? "payroll.import.execute" : "payroll.import.view", token);
        }
        else
            await access.EnsureAsync(businessId,
                write ? HrOrganisationOperation.ImportEmployees : HrOrganisationOperation.ViewImports, null, token);
        return businessId;
    }

    private async Task AuthorizeRowAsync(long businessId, string importType, HrOpeningImportRow row, CancellationToken token)
    {
        if (importType == "Employee")
        {
            await employeeAccess.EnsureAsync(businessId, HrEmployeeOperation.Create, token);
            if (row.Employee is not null)
                CreateHrEmployeeCommandHandler.EnsureSensitiveWriteAccess(row.Employee, employeeAccess.GetFieldAccess());
            if (row.LocationId is null
                && (await access.GetLocationReadScopeAsync(businessId, token)).AllowedSources is not null)
                throw new UnauthorizedAccessException("Unassigned employee opening requires Business-wide location access.");
            if (row.LocationId.HasValue && row.AssignmentType != "Primary"
                && (await access.GetLocationReadScopeAsync(businessId, token)).AllowedSources is not null)
                throw new UnauthorizedAccessException("An Additional assignment does not establish employee access.");
        }
        if (row.LocationId.HasValue)
            await access.EnsureAsync(businessId, HrOrganisationOperation.ManageEmployeeLocationAssignments, row.LocationId, token);
        if (row.PositionId.HasValue)
        {
            await employeeAccess.EnsureAsync(businessId, HrEmployeeOperation.ManagePositionAssignments, token);
        }
        if (importType == "Assignment")
        {
            var employeeId = await store.FindEmployeeIdAsync(businessId, row.EmployeeCode, token);
            if (employeeId.HasValue)
                await employeeAccess.EnsureEmployeeLocationAccessAsync(businessId, employeeId.Value, token);
        }
    }

    private async Task EnsurePayrollRowAccessAsync(
        long businessId, string importType, HrOpeningImportRow row, bool write, CancellationToken token)
    {
        if (importType is not ("Salary" or "LoanOpening")) return;
        if (payrollAccess is null || salaries is null)
            throw new HrImportStorageUnavailableException();
        await payrollAccess.EnsureBusinessPermissionAsync(businessId,
            write ? "payroll.import.execute" : "payroll.import.view", token);
        var employeeId = await store.FindEmployeeIdAsync(businessId, row.EmployeeCode, token)
            ?? throw new KeyNotFoundException("Employee was not found in this Business.");
        var effectiveOn = importType == "Salary"
            ? row.Salary?.EffectiveFrom ?? throw new ArgumentException("Salary row is invalid.")
            : row.Loan?.Request.RecoveryStartDate ?? throw new ArgumentException("Loan row is invalid.");
        var locationId = await salaries.EmployeePrimaryLocationAsync(
            businessId, employeeId, effectiveOn, token);
        await payrollAccess.EnsureAsync(businessId, locationId,
            write ? "payroll.import.execute" : "payroll.import.view", token);
        var permission = importType == "Salary"
            ? write ? "payroll.salary.manage" : "payroll.salary.view"
            : write ? "payroll.loan.manage" : "payroll.loan.view";
        await payrollAccess.EnsureAsync(businessId, locationId, permission, token);
    }

    private void RequireStorage()
    {
        if (blobs is null || encryption is null || inspector is null || limits is null
            || limits.MaxBytes is <= 0 or > int.MaxValue || policy is null
            || policy.MaxRows is < 1 or > 1000 || policy.RetentionDays <= 0)
            throw new HrImportStorageUnavailableException();
    }

    private static RequiredAuditOperation Operation(long businessId, string action, string importType) =>
        new(businessId,
            importType is "Salary" or "LoanOpening" ? "payroll.import." + action : "hr.import." + action,
            importType is "Salary" or "LoanOpening" ? "Payroll" : "HR",
            "Opening " + (importType is "Salary" or "LoanOpening" ? "Payroll" : "HR")
                + " import " + action,
            "HrImportBatch");
}

public sealed class HrImportStorageUnavailableException()
    : InvalidOperationException("Private import storage, encryption, scanner, limits and retention composition are not approved.");

public sealed class HrImportExecutionException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
