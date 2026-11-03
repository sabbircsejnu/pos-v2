using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Employees;

public sealed class HrOpeningImportStore(
    RetailPOSDbContext db, IHrEmployeeStore employees,
    IHrEmployeeLocationAssignmentStore locations, TimeProvider clock,
    IPayrollSalarySetupStore? salaries = null,
    IPayrollEmployeeLoanStore? loans = null) : IHrOpeningImportStore
{
    public async Task LockAsync(long businessId, string key, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Import serialization requires the required audit transaction.");
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ long.MinValue})", token);
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new InvalidOperationException("Import execution requires PostgreSQL or SQLite test transactions.");
    }

    public async Task<HrImportBatchResult?> FindByKeyAsync(long businessId, string key,
        string hash, string importType, string templateVersion, CancellationToken token)
    {
        var batch = await db.Set<HrImportBatch>().AsNoTracking().SingleOrDefaultAsync(
            b => b.BusinessId == businessId && b.IdempotencyKey == key, token);
        if (batch is null) return null;
        if (batch.ContentHash != hash || batch.ImportType != importType || batch.TemplateVersion != templateVersion)
            throw new InvalidOperationException("Idempotency key already belongs to different import content or template.");
        return await GetAsync(businessId, batch.Id, token);
    }

    public async Task<HrImportBatchResult> CreateAsync(long businessId, long actorId, string key,
        string importType, string templateVersion, HrImportFile file, CancellationToken token)
    {
        var batch = new HrImportBatch
        {
            BusinessId = businessId, ImportType = importType, TemplateVersion = templateVersion,
            IdempotencyKey = key, ContentHash = file.ContentHash, FileStorageKey = file.StorageKey,
            EncryptionKeyReference = file.EncryptionKeyReference, CreatedBy = actorId,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime, RetainUntilUtc = file.RetainUntilUtc.UtcDateTime
        };
        db.Add(batch);
        await db.SaveChangesAsync(token);
        return await GetAsync(businessId, batch.Id, token);
    }

    public async Task<HrImportSource> GetSourceAsync(long businessId, long id, CancellationToken token)
    {
        var batch = await FindBatchAsync(businessId, id, token);
        return new(id, batch.ImportType, batch.TemplateVersion,
            new(batch.FileStorageKey, batch.EncryptionKeyReference, batch.ContentHash,
                Utc(batch.RetainUntilUtc)), batch.Status, batch.CreatedBy);
    }

    public async Task<HrImportBatchResult> GetAsync(long businessId, long id, CancellationToken token)
    {
        var batch = await FindBatchAsync(businessId, id, token);
        var rows = await db.Set<HrImportRow>().AsNoTracking()
            .Where(r => r.BusinessId == businessId && r.ImportBatchId == id && r.Revision == batch.Revision)
            .OrderBy(r => r.RowNo).ToArrayAsync(token);
        return new(id, batch.ImportType, batch.TemplateVersion, batch.Status, batch.IsDryRun,
            batch.TotalRows, batch.ValidRows, batch.InvalidRows, batch.Revision, batch.Version,
            Utc(batch.CreatedAtUtc), batch.ExecutedAtUtc.HasValue ? Utc(batch.ExecutedAtUtc.Value) : null,
            rows.Select(r => new HrImportRowResult(r.RowNo, r.ExternalKey, r.ValidationStatus,
                JsonSerializer.Deserialize<string[]>(r.ErrorJson)!, r.CreatedEntityType, r.CreatedEntityId)).ToArray());
    }

    private static string ImportRequestKey(long batchId, string externalKey) =>
        $"import-{batchId}-{externalKey}";

    public Task<long?> FindEmployeeIdAsync(long businessId, string code, CancellationToken token) =>
        db.HrEmployees.AsNoTracking().Where(e => e.BusinessId == businessId && e.EmployeeCode == code)
            .Select(e => (long?)e.Id).SingleOrDefaultAsync(token);

    public async Task<IReadOnlyList<string>> ValidateAsync(long businessId, string importType,
        HrOpeningImportRow row, CancellationToken token)
    {
        var errors = new List<string>();
        var employee = await db.HrEmployees.AsNoTracking().SingleOrDefaultAsync(
            e => e.BusinessId == businessId && e.EmployeeCode == row.EmployeeCode, token);
        if (importType == "Employee")
        {
            if (employee is not null) errors.Add("Employee code already exists in this Business.");
            if (row.Employee is null || !await db.HrEmploymentTypes.AnyAsync(
                t => t.BusinessId == businessId && t.Id == row.Employee.EmploymentTypeId && t.IsActive, token))
                errors.Add("Employment type is invalid or inactive in this Business.");
        }
        else
        {
            if (employee is null) errors.Add("Employee code does not exist in this Business.");
            else if (employee.CurrentEmploymentStatus == "Exited" || row.EffectiveFrom < employee.JoiningDate)
                errors.Add("Employee is exited or assignment precedes joining.");
            if (employee is not null && !await db.HrEmploymentPeriods.AnyAsync(
                p => p.BusinessId == businessId && p.EmployeeId == employee.Id && p.Status == "Active", token))
                errors.Add("Employee has no active employment period.");
        }
        if (row.PositionId.HasValue)
        {
            var position = await db.HrPositions.AsNoTracking().SingleOrDefaultAsync(
                p => p.BusinessId == businessId && p.Id == row.PositionId, token);
            if (position is null || position.OrgUnitId != row.OrgUnitId || position.Status == HrPositionStatus.Inactive)
                errors.Add("Position/Department reference is invalid or inactive in this Business.");
            var org = await db.HrOrgUnits.AsNoTracking().SingleOrDefaultAsync(
                o => o.BusinessId == businessId && o.Id == row.OrgUnitId, token);
            if (org is null || !org.IsActive || org.EffectiveFrom > row.EffectiveFrom || org.EffectiveTo < row.EffectiveFrom)
                errors.Add("Department / Team is invalid, inactive or ineffective in this Business.");
        }
        if (row.LocationId.HasValue)
        {
            var location = await db.OperationalLocationProjections.AsNoTracking().SingleOrDefaultAsync(
                l => l.BusinessId == businessId && l.Id == row.LocationId, token);
            if (location is null || !location.IsSourceActive || location.IsSourceDeleted
                || location.SyncStatus != "Current" || location.SourceEntityType is not ("Outlet" or "Warehouse"))
                errors.Add("Location projection is invalid, inactive, unsupported or not owned by this Business.");
            else if (!long.TryParse(location.SourceEntityId, out var sourceId)
                || !(location.SourceEntityType == "Outlet"
                    ? await db.Outlets.AnyAsync(o => o.BusinessId == businessId && o.Id == sourceId, token)
                    : await db.Warehouses.AnyAsync(w => w.BusinessId == businessId && w.Id == sourceId, token)))
                errors.Add("The operational location source is not active or owned by this Business.");
            if (employee is not null && row.AssignmentType == "Primary"
                && await db.HrEmployeeLocationAssignments.AnyAsync(a => a.BusinessId == businessId
                    && a.EmployeeId == employee.Id && a.AssignmentType == "Primary"
                    && (!a.EffectiveTo.HasValue || a.EffectiveTo >= row.EffectiveFrom), token))
                errors.Add("Primary location conflicts with existing history; use the transfer workflow.");
        }
        if (row.EffectiveFrom > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)
            && (row.LocationId.HasValue || row.PositionId.HasValue))
            errors.Add("Opening assignments cannot be future-effective.");
        if (importType == "Salary")
            errors.AddRange(await ValidateSalaryAsync(businessId, row, token));
        else if (importType == "LoanOpening")
            errors.AddRange(await ValidateLoanAsync(businessId, row, token));
        return errors;
    }

    private async Task<IReadOnlyList<string>> ValidateSalaryAsync(
        long businessId, HrOpeningImportRow row, CancellationToken token)
    {
        var errors = new List<string>();
        if (row.Salary is null) return ["Salary row is invalid."];
        var employee = await db.HrEmployees.AsNoTracking().SingleOrDefaultAsync(
            item => item.BusinessId == businessId && item.EmployeeCode == row.EmployeeCode, token);
        if (employee is null)
        {
            errors.Add("Employee does not exist in this Business.");
            return errors;
        }
        if (employee.CurrentEmploymentStatus != "Active"
            || !await db.HrEmploymentPeriods.AsNoTracking().AnyAsync(period =>
                period.BusinessId == businessId && period.EmployeeId == employee.Id
                && period.StartDate <= row.Salary.EffectiveFrom
                && (period.EndDate == null || period.EndDate >= row.Salary.EffectiveFrom), token))
            errors.Add("Employee is not active on the salary effective date.");
        var structure = await db.PayrollSalaryStructures.AsNoTracking()
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.BusinessId == businessId
                && item.Id == row.Salary.SalaryStructureId && item.IsActive, token);
        if (structure is null)
        {
            errors.Add("Active salary structure was not found in this Business.");
            return errors;
        }
        if (await db.PayrollEmployeeSalaryVersions.AsNoTracking().AnyAsync(item =>
                item.BusinessId == businessId && item.EmployeeId == employee.Id)
            || await db.PayrollSalarySetupRequests.AsNoTracking().AnyAsync(item =>
                item.BusinessId == businessId && item.EmployeeId == employee.Id
                && item.Status == "PendingApproval"))
            errors.Add("Employee already has an effective or pending initial salary setup.");
        var requested = row.Salary.Components.Select(item => item.SalaryComponentId).Order().ToArray();
        var structureIds = structure.Lines.Select(item => item.SalaryComponentId).Order().ToArray();
        if (!requested.SequenceEqual(structureIds)
            || requested.Distinct().Count() != requested.Length)
        {
            errors.Add("Salary component values must exactly match the active structure.");
            return errors;
        }
        var values = row.Salary.Components.ToDictionary(item => item.SalaryComponentId, item => item.Value);
        var componentIds = structure.Lines.Select(item => item.SalaryComponentId).ToList();
        var components = await db.PayrollSalaryComponents.AsNoTracking()
            .Where(item => item.BusinessId == businessId && componentIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, token);
        foreach (var line in structure.Lines)
        {
            if (!components.TryGetValue(line.SalaryComponentId, out var component) || !component.IsActive)
            {
                errors.Add("Salary structure contains an inactive or invalid component.");
                continue;
            }
            var value = values[line.SalaryComponentId] ?? line.DefaultValue;
            if (component.ValueType is "Formula" or "Calculated")
            {
                if (values[line.SalaryComponentId].HasValue)
                    errors.Add($"Calculated component {component.Code} cannot have an imported manual value.");
            }
            else if (!value.HasValue)
                errors.Add($"Component {component.Code} requires an imported value.");
            if (value.HasValue && (value.Value < 0 || value.Value > 99999999999999.9999m
                || value.Value != decimal.Round(value.Value, 4)))
                errors.Add($"Component {component.Code} has an invalid amount.");
        }
        return errors;
    }

    private async Task<IReadOnlyList<string>> ValidateLoanAsync(
        long businessId, HrOpeningImportRow row, CancellationToken token)
    {
        var errors = new List<string>();
        if (row.Loan is null) return ["Loan-opening row is invalid."];
        var employee = await db.HrEmployees.AsNoTracking().SingleOrDefaultAsync(
            item => item.BusinessId == businessId && item.EmployeeCode == row.EmployeeCode, token);
        if (employee is null)
        {
            errors.Add("Employee does not exist in this Business.");
            return errors;
        }
        if (employee.CurrentEmploymentStatus != "Active"
            || !await db.HrEmploymentPeriods.AsNoTracking().AnyAsync(period =>
                period.BusinessId == businessId && period.EmployeeId == employee.Id
                && period.StartDate <= row.Loan.Request.RecoveryStartDate
                && (period.EndDate == null || period.EndDate >= row.Loan.Request.RecoveryStartDate), token))
            errors.Add("Employee is not active on the recovery start date.");
        if (row.Loan.DisbursedDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            errors.Add("Historical disbursement date cannot be in the future.");
        if (row.Loan.OutstandingAmount > row.Loan.PrincipalAmount)
            errors.Add("Opening balance cannot exceed original principal.");
        var schedule = row.Loan.Request.Schedule;
        if (row.Loan.Request.RecoveryMode == "CustomSchedule"
            && (schedule.Count is < 1 or > 120
                || schedule.Any(item => item.DueDate < row.Loan.Request.RecoveryStartDate
                    || item.PlannedAmount <= 0 || item.PlannedAmount > 99999999999999.9999m
                    || item.PlannedAmount != decimal.Round(item.PlannedAmount, 4))
                || schedule.Select(item => item.DueDate).Distinct().Count() != schedule.Count
                || decimal.Round(schedule.Sum(item => item.PlannedAmount), 4) != row.Loan.OutstandingAmount))
            errors.Add("Custom recovery schedule must validly recover the opening balance.");
        if (row.Loan.Request.RecoveryMode == "FixedMonthly"
            && row.Loan.Request.FixedInstallment is <= 0)
            errors.Add("Fixed monthly recovery requires a positive installment.");
        return errors;
    }

    public async Task<HrImportBatchResult> RecordValidationAsync(long businessId, long id,
        IReadOnlyList<HrImportRowResult> rows, CancellationToken token)
    {
        var batch = await FindBatchAsync(businessId, id, token);
        batch.Revision++;
        batch.TotalRows = rows.Count;
        batch.InvalidRows = rows.Count(r => r.Errors.Count > 0);
        batch.ValidRows = rows.Count - batch.InvalidRows;
        batch.Status = batch.InvalidRows == 0 ? "Ready" : "ValidationFailed";
        batch.IsDryRun = true;
        batch.Version++;
        AddEvidence(batch, rows);
        await db.SaveChangesAsync(token);
        return await GetAsync(businessId, id, token);
    }

    public async Task<HrImportBatchResult> ExecuteAsync(long businessId, long actorId, long id,
        IReadOnlyList<HrOpeningImportRow> rows, CancellationToken token)
    {
        var batch = await FindBatchAsync(businessId, id, token);
        if (batch.Status != "Ready") throw new InvalidOperationException("Import is not ready.");
        batch.Status = "Processing";
        batch.IsDryRun = false;
        var results = new List<HrImportRowResult>();
        try
        {
            foreach (var row in rows)
            {
                long employeeId;
                long version;
                if (batch.ImportType == "Employee")
                {
                    var employee = await employees.CreateAsync(businessId, actorId, row.Employee!,
                        new(false, false), token);
                    employeeId = employee.Id; version = employee.Version;
                    if (row.PositionId.HasValue)
                    {
                        await employees.AssignPositionAsync(businessId, actorId, employeeId, version,
                            new(row.PositionId.Value, row.OrgUnitId!.Value, null, "Primary",
                                row.EffectiveFrom, null, $"Opening import batch {id} row {row.RowNo}"), token);
                        version++;
                    }
                }
                else
                {
                    employeeId = await FindEmployeeIdAsync(businessId, row.EmployeeCode, token)
                        ?? throw new InvalidOperationException("Assignment employee is no longer available.");
                    version = (await employees.GetProfileAsync(businessId, employeeId, new(false, false), token))!.Version;
                }
                if (batch.ImportType == "Salary")
                {
                    var employeeIdForSalary = await FindEmployeeIdAsync(businessId, row.EmployeeCode, token)
                        ?? throw new InvalidOperationException("Salary import employee is no longer available.");
                    var salary = row.Salary ?? throw new InvalidOperationException("Salary row has no proposal.");
                    var request = new CreatePayrollSalarySetupRequest(
                        employeeIdForSalary, salary.SalaryStructureId, salary.EffectiveFrom,
                        salary.Components, salary.Reason, ImportRequestKey(batch.Id, row.ExternalKey));
                    if (salaries is null)
                        throw new InvalidOperationException("Payroll salary setup composition is unavailable.");
                    var setup = await salaries.CreateInitialRequestAsync(businessId, actorId, request, token);
                    results.Add(new(row.RowNo, row.ExternalKey, "Completed", [],
                        "PayrollSalarySetupRequest", setup.Id));
                    continue;
                }
                if (batch.ImportType == "LoanOpening")
                {
                    var employeeIdForLoan = await FindEmployeeIdAsync(businessId, row.EmployeeCode, token)
                        ?? throw new InvalidOperationException("Loan import employee is no longer available.");
                    var loan = row.Loan ?? throw new InvalidOperationException("Loan row has no proposal.");
                    if (loans is null)
                        throw new InvalidOperationException("Payroll loan composition is unavailable.");
                    var request = loan.Request with
                    {
                        EmployeeId = employeeIdForLoan,
                        IdempotencyKey = ImportRequestKey(batch.Id, row.ExternalKey)
                    };
                    var created = await loans.CreateOpeningAsync(businessId, actorId, request,
                        loan.DisbursedDate, loan.OutstandingAmount, ImportRequestKey(batch.Id, row.ExternalKey), token);
                    results.Add(new(row.RowNo, row.ExternalKey, "Completed", [],
                        "PayrollEmployeeLoan", created.Id));
                    continue;
                }
                long? assignmentId = null;
                if (row.LocationId.HasValue)
                {
                    var assignment = await locations.CreateAsync(businessId, actorId, employeeId, version,
                        new(row.LocationId.Value, row.AssignmentType, false, row.EffectiveFrom, null,
                            $"Opening import batch {id} row {row.RowNo}"), token)
                        ?? throw new InvalidOperationException("Assignment employee is no longer available.");
                    assignmentId = assignment.Id;
                }
                results.Add(new(row.RowNo, row.ExternalKey, "Completed", [],
                    batch.ImportType == "Employee" ? "HrEmployee" : "HrEmployeeLocationAssignment",
                    batch.ImportType == "Employee" ? employeeId : assignmentId));
            }
            batch.Status = "Completed"; batch.Revision++; batch.Version++;
            batch.ExecutedBy = actorId; batch.ExecutedAtUtc = clock.GetUtcNow().UtcDateTime;
            AddEvidence(batch, results);
            await db.SaveChangesAsync(token);
            return await GetAsync(businessId, id, token);
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or HrEmployeeConcurrencyException)
        {
            throw new HrImportExecutionException("Opening import execution conflicted; the entire batch was rolled back.", exception);
        }
    }

    public async Task RecordFailureAsync(long businessId, long id, CancellationToken token)
    {
        var batch = await FindBatchAsync(businessId, id, token);
        if (batch.Status == "Completed") return;
        var previous = await GetAsync(businessId, id, token);
        batch.Status = "Failed"; batch.Version++; batch.Revision++;
        batch.IsDryRun = false;
        batch.InvalidRows = batch.TotalRows; batch.ValidRows = 0;
        AddEvidence(batch, previous.Rows.Select(row => new HrImportRowResult(
            row.RowNo, row.ExternalKey, "Invalid", ["Execution failed; all rows were rolled back."])).ToArray());
        await db.SaveChangesAsync(token);
    }

    private void AddEvidence(HrImportBatch batch, IReadOnlyList<HrImportRowResult> rows)
    {
        db.AddRange(rows.Select(row => new HrImportRow
        {
            BusinessId = batch.BusinessId, ImportBatchId = batch.Id, Revision = batch.Revision,
            RowNo = row.RowNo, ExternalKey = row.ExternalKey, ValidationStatus = row.Status,
            ErrorJson = JsonSerializer.Serialize(row.Errors),
            CreatedEntityType = row.CreatedEntityType, CreatedEntityId = row.CreatedEntityId
        }));
    }

    private async Task<HrImportBatch> FindBatchAsync(long businessId, long id, CancellationToken token)
    {
        var batch = await db.Set<HrImportBatch>().SingleOrDefaultAsync(
            b => b.BusinessId == businessId && b.Id == id, token)
            ?? throw new KeyNotFoundException("Import batch was not found in this Business.");
        // A waiting import may already have tracked the pre-lock state in this scope.
        if (db.Entry(batch).State == EntityState.Unchanged)
            await db.Entry(batch).ReloadAsync(token);
        return batch;
    }

    private static DateTimeOffset Utc(DateTime value) => new(
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime());
}
