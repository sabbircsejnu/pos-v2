using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using Npgsql;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Employees;

public sealed partial class HrEmployeeStore(RetailPOSDbContext db) : IHrEmployeeStore
{
    public async Task<IReadOnlyList<HrEmploymentTypeReadModel>> GetEmploymentTypesAsync(
        long businessId, CancellationToken cancellationToken) =>
        await db.HrEmploymentTypes.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.IsActive)
            .OrderBy(row => row.Name)
            .Select(row => new HrEmploymentTypeReadModel(row.Id, row.Code, row.Name))
            .ToArrayAsync(cancellationToken);

    public async Task<HrEmployeeSearchPage> SearchAsync(
        long businessId,
        string? search,
        int page,
        int pageSize,
        IReadOnlyCollection<OperationalLocationSourceIdentity>? allowedLocations,
        CancellationToken cancellationToken)
    {
        var query = db.HrEmployees.AsNoTracking()
            .Where(row => row.BusinessId == businessId);
        if (allowedLocations is not null)
        {
            if (allowedLocations.Count == 0)
                return new HrEmployeeSearchPage([], page, pageSize, 0);
            var sources = allowedLocations.ToHashSet();
            var allowedIds = await db.OperationalLocationProjections.AsNoTracking()
                .Where(location => location.BusinessId == businessId)
                .Select(location => new { location.Id, location.SourceEntityType, location.SourceEntityId })
                .ToArrayAsync(cancellationToken);
            var projectionIds = allowedIds
                .Where(location => sources.Contains(new OperationalLocationSourceIdentity(
                    location.SourceEntityType, location.SourceEntityId)))
                .Select(location => location.Id).ToArray();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            query = query.Where(employee => db.HrEmployeeLocationAssignments.Any(assignment =>
                assignment.BusinessId == businessId && assignment.EmployeeId == employee.Id
                && assignment.Status == "Effective"
                && assignment.AssignmentType == "Primary"
                && assignment.EffectiveFrom <= today
                && (!assignment.EffectiveTo.HasValue
                    || assignment.EffectiveTo >= today)
                && Enumerable.Contains(projectionIds, assignment.OperationalLocationProjectionId))
                || (employee.CurrentEmploymentStatus == "Exited"
                    && !db.HrEmployeeLocationAssignments.Any(assignment =>
                        assignment.BusinessId == businessId && assignment.EmployeeId == employee.Id
                        && assignment.AssignmentType == "Primary"
                        && assignment.Status == "Effective" && assignment.EffectiveFrom <= today
                        && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= today))
                    && db.HrEmployeeLocationAssignments.Any(assignment =>
                        assignment.BusinessId == businessId && assignment.EmployeeId == employee.Id
                        && assignment.AssignmentType == "Primary"
                        && Enumerable.Contains(projectionIds, assignment.OperationalLocationProjectionId))));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(row => row.EmployeeCode.ToLower().Contains(term)
                || row.DisplayName.ToLower().Contains(term)
                || (row.WorkEmail != null && row.WorkEmail.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var employees = await query.OrderBy(row => row.EmployeeCode).ThenBy(row => row.Id)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Join(db.HrEmploymentTypes.AsNoTracking(),
                employee => new { employee.BusinessId, Id = employee.CurrentEmploymentTypeId },
                type => new { type.BusinessId, type.Id },
                (employee, type) => new HrEmployeeSearchItem(
                    employee.Id, employee.EmployeeCode, employee.FirstName, employee.MiddleName,
                    employee.LastName, employee.DisplayName, employee.WorkEmail,
                    employee.CurrentEmploymentStatus, employee.CurrentEmploymentTypeId,
                    type.Name, employee.JoiningDate, employee.Version))
            .ToArrayAsync(cancellationToken);
        return new HrEmployeeSearchPage(employees, page, pageSize, total);
    }

    public Task<HrEmployeeProfile?> GetProfileAsync(
        long businessId,
        long employeeId,
        HrEmployeeFieldAccess fieldAccess,
        CancellationToken cancellationToken) =>
        db.HrEmployees.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == employeeId)
            .Join(db.HrEmploymentTypes.AsNoTracking(),
                employee => new { employee.BusinessId, Id = employee.CurrentEmploymentTypeId },
                type => new { type.BusinessId, type.Id },
                (employee, type) => new HrEmployeeProfile(
                    employee.Id, employee.EmployeeCode, employee.FirstName, employee.MiddleName,
                    employee.LastName, employee.DisplayName, employee.WorkEmail,
                    employee.CurrentEmploymentStatus, employee.CurrentEmploymentTypeId,
                    type.Name, employee.CurrentPositionId, employee.CurrentOrgUnitId,
                    employee.JoiningDate, employee.ConfirmationDate, employee.Version,
                    new HrEmployeeSensitiveFields(
                        fieldAccess.CanViewSensitive ? employee.DateOfBirth : null,
                        fieldAccess.CanViewSensitive ? employee.GenderCode : null,
                        fieldAccess.CanViewSensitive ? employee.PersonalEmail : null,
                        fieldAccess.CanViewSensitive ? employee.Mobile : null,
                        fieldAccess.CanViewSensitive ? employee.PresentAddress : null,
                        fieldAccess.CanViewSensitive ? employee.PermanentAddress : null,
                        fieldAccess.CanViewSensitive ? employee.EmergencyContactName : null,
                        fieldAccess.CanViewSensitive ? employee.EmergencyContactPhone : null,
                        fieldAccess.CanViewIdentity ? employee.NationalId : null,
                        fieldAccess.CanViewIdentity ? employee.PassportNo : null)))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<HrEmployeeProfile> CreateAsync(
        long businessId,
        long actorUserId,
        HrEmployeeCreateData data,
        HrEmployeeFieldAccess fieldAccess,
        CancellationToken cancellationToken)
    {
        if (!await db.HrEmploymentTypes.AnyAsync(row =>
                row.BusinessId == businessId && row.Id == data.EmploymentTypeId && row.IsActive,
                cancellationToken))
            throw new InvalidOperationException("Employment type was not found or is inactive in this Business.");

        var employeeCode = data.EmployeeCode.Trim().ToUpperInvariant();
        if (await db.HrEmployees.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeCode == employeeCode,
                cancellationToken))
            throw new InvalidOperationException("Employee code already exists in this Business.");

        var employee = new HrEmployee
        {
            BusinessId = businessId,
            EmployeeCode = employeeCode,
            FirstName = data.FirstName.Trim(),
            MiddleName = NullIfBlank(data.MiddleName),
            LastName = NullIfBlank(data.LastName),
            DisplayName = data.DisplayName.Trim(),
            DateOfBirth = data.DateOfBirth,
            GenderCode = NullIfBlank(data.GenderCode),
            NationalId = NullIfBlank(data.NationalId),
            PassportNo = NullIfBlank(data.PassportNo),
            PersonalEmail = NullIfBlank(data.PersonalEmail),
            WorkEmail = NullIfBlank(data.WorkEmail),
            Mobile = NullIfBlank(data.Mobile),
            PresentAddress = NullIfBlank(data.PresentAddress),
            PermanentAddress = NullIfBlank(data.PermanentAddress),
            EmergencyContactName = NullIfBlank(data.EmergencyContactName),
            EmergencyContactPhone = NullIfBlank(data.EmergencyContactPhone),
            CurrentEmploymentStatus = data.EmploymentStatus,
            CurrentEmploymentTypeId = data.EmploymentTypeId,
            JoiningDate = data.JoiningDate,
            ConfirmationDate = data.ConfirmationDate,
            Version = 1
        };
        db.HrEmployees.Add(employee);
        var period = new HrEmploymentPeriod
        {
            BusinessId = businessId,
            Employee = employee,
            EmploymentTypeId = data.EmploymentTypeId,
            StartDate = data.JoiningDate,
            Status = "Active",
            SourceEventType = "Joined"
        };
        db.HrEmploymentPeriods.Add(period);
        db.HrEmployeeLifecycleEvents.Add(new HrEmployeeLifecycleEvent
        {
            BusinessId = businessId,
            Employee = employee,
            EmploymentPeriod = period,
            EventType = "Joined",
            EffectiveDate = data.JoiningDate,
            PreviousStateJson = "{}",
            NewStateJson = HrEmployeeHistoryJson.State("Active", data.EmploymentTypeId,
                null, null, null),
            ActorUserId = actorUserId,
            OccurredAtUtc = DateTime.UtcNow
        });
        if (data.EmploymentStatus == "Probation")
        {
            db.HrEmployeeLifecycleEvents.Add(new HrEmployeeLifecycleEvent
            {
                BusinessId = businessId,
                Employee = employee,
                EmploymentPeriod = period,
                EventType = "Probation",
                EffectiveDate = data.JoiningDate,
                PreviousStateJson = HrEmployeeHistoryJson.State(
                    "Active", data.EmploymentTypeId, null, null, null),
                NewStateJson = HrEmployeeHistoryJson.State(
                    "Probation", data.EmploymentTypeId, null, null, null),
                ActorUserId = actorUserId,
                OccurredAtUtc = DateTime.UtcNow
            });
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsEmployeeCodeConflict(exception))
        {
            throw new InvalidOperationException("Employee code already exists in this Business.", exception);
        }
        return await GetProfileAsync(businessId, employee.Id, fieldAccess, cancellationToken)
            ?? throw new InvalidOperationException("Created employee could not be read.");
    }

    public async Task<HrEmployeeProfile?> UpdateAsync(
        long businessId,
        long employeeId,
        long expectedVersion,
        HrEmployeeUpdateData data,
        HrEmployeeFieldAccess fieldAccess,
        CancellationToken cancellationToken)
    {
        var employee = await db.HrEmployees.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == employeeId, cancellationToken);
        if (employee is null)
            return null;
        if (employee.Version != expectedVersion)
            throw new HrEmployeeConcurrencyException("Employee changed since it was read.");

        employee.FirstName = data.FirstName.Trim();
        employee.MiddleName = NullIfBlank(data.MiddleName);
        employee.LastName = NullIfBlank(data.LastName);
        employee.DisplayName = data.DisplayName.Trim();
        employee.WorkEmail = NullIfBlank(data.WorkEmail);
        if (fieldAccess.CanViewSensitive)
        {
            employee.DateOfBirth = data.DateOfBirth;
            employee.GenderCode = NullIfBlank(data.GenderCode);
            employee.PersonalEmail = NullIfBlank(data.PersonalEmail);
            employee.Mobile = NullIfBlank(data.Mobile);
            employee.PresentAddress = NullIfBlank(data.PresentAddress);
            employee.PermanentAddress = NullIfBlank(data.PermanentAddress);
            employee.EmergencyContactName = NullIfBlank(data.EmergencyContactName);
            employee.EmergencyContactPhone = NullIfBlank(data.EmergencyContactPhone);
        }
        if (fieldAccess.CanViewIdentity)
        {
            employee.NationalId = NullIfBlank(data.NationalId);
            employee.PassportNo = NullIfBlank(data.PassportNo);
        }
        employee.Version = checked(employee.Version + 1);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new HrEmployeeConcurrencyException("Employee was changed by another request.", exception);
        }
        return await GetProfileAsync(businessId, employee.Id, fieldAccess, cancellationToken);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsEmployeeCodeConflict(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && postgres.ConstraintName?.Contains("hr_employees_business_id_employee_code",
                    StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }
        return false;
    }
}
