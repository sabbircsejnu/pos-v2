using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Employees;

public sealed partial class HrEmployeeStore
{
    public async Task<IReadOnlyList<HrEmployeeLifecycleEventReadModel>> GetLifecycleHistoryAsync(
        long businessId, long employeeId, CancellationToken cancellationToken) =>
        await db.HrEmployeeLifecycleEvents.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId)
            .OrderBy(row => row.EffectiveDate).ThenBy(row => row.Id)
            .Select(row => new HrEmployeeLifecycleEventReadModel(
                row.Id, row.EmployeeId, row.EmploymentPeriodId, row.EventType, row.EffectiveDate,
                row.PreviousStateJson, row.NewStateJson, row.Reason, row.ActorUserId,
                new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc))))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<HrEmployeePositionAssignmentReadModel>> GetPositionHistoryAsync(
        long businessId, long employeeId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await (from assignment in db.HrEmployeePositionAssignments.AsNoTracking()
               where assignment.BusinessId == businessId && assignment.EmployeeId == employeeId
               join position in db.HrPositions.AsNoTracking()
                   on new { assignment.BusinessId, assignment.PositionId }
                   equals new { position.BusinessId, PositionId = position.Id }
               join orgUnit in db.HrOrgUnits.AsNoTracking()
                   on new { assignment.BusinessId, assignment.OrgUnitId }
                   equals new { orgUnit.BusinessId, OrgUnitId = orgUnit.Id }
               join manager in db.HrEmployees.AsNoTracking()
                   on new { assignment.BusinessId, EmployeeId = assignment.ReportsToEmployeeId.GetValueOrDefault() }
                   equals new { manager.BusinessId, EmployeeId = manager.Id } into managers
               from manager in managers.DefaultIfEmpty()
               orderby assignment.EffectiveFrom descending, assignment.Id descending
               select new HrEmployeePositionAssignmentReadModel(
                   assignment.Id, assignment.EmployeeId, assignment.PositionId,
                   position.Code, position.Name, assignment.OrgUnitId, orgUnit.Name,
                   assignment.ReportsToEmployeeId, manager == null ? null : manager.DisplayName,
                   assignment.AssignmentType.ToString(), assignment.EffectiveFrom, assignment.EffectiveTo,
                   assignment.Reason,
                   assignment.EffectiveFrom <= today
                       && (!assignment.EffectiveTo.HasValue
                           || assignment.EffectiveTo.GetValueOrDefault() >= today),
                   assignment.Version))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<HrEmployeeLifecycleEventReadModel?> TransitionLifecycleAsync(
        long businessId,
        long actorUserId,
        long employeeId,
        long expectedVersion,
        HrEmployeeLifecycleTransitionData data,
        CancellationToken cancellationToken)
    {
        await LockHistoryScopeAsync(businessId, cancellationToken);
        var employee = await db.HrEmployees.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == employeeId, cancellationToken);
        if (employee is null)
            return null;
        RequireExpectedVersion(employee, expectedVersion);
        if (employee.CurrentEmploymentStatus == "Exited")
            throw new InvalidOperationException("An exited employee must be rehired before another lifecycle transition.");

        var period = await db.HrEmploymentPeriods.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.EmployeeId == employeeId && row.Status == "Active",
            cancellationToken)
            ?? throw new InvalidOperationException("Employee has no active employment period.");
        ValidateEffectiveDate(data.EffectiveDate, period.StartDate);
        var previousState = await CurrentStateAsync(employee, data.EffectiveDate, cancellationToken);

        switch (data.EventType)
        {
            case "Probation":
                if (employee.CurrentEmploymentStatus != "Active")
                    throw new InvalidOperationException("Only an active employee can enter probation.");
                employee.CurrentEmploymentStatus = "Probation";
                break;
            case "Confirmed":
                if (employee.CurrentEmploymentStatus != "Probation")
                    throw new InvalidOperationException("Only an employee on probation can be confirmed.");
                employee.CurrentEmploymentStatus = "Active";
                employee.ConfirmationDate = data.EffectiveDate;
                break;
            case "EmploymentTypeChanged":
                if (!await db.HrEmploymentTypes.AnyAsync(row =>
                        row.BusinessId == businessId && row.Id == data.EmploymentTypeId && row.IsActive,
                        cancellationToken))
                    throw new InvalidOperationException("Employment type was not found or is inactive in this Business.");
                employee.CurrentEmploymentTypeId = data.EmploymentTypeId!.Value;
                break;
            case "Suspended":
                if (employee.CurrentEmploymentStatus is not ("Active" or "Probation"))
                    throw new InvalidOperationException("Only an active or probationary employee can be suspended.");
                employee.SuspendedFromStatus = employee.CurrentEmploymentStatus;
                employee.CurrentEmploymentStatus = "Suspended";
                break;
            case "Reinstated":
                if (employee.CurrentEmploymentStatus != "Suspended" || employee.SuspendedFromStatus is null)
                    throw new InvalidOperationException("Only a suspended employee can be reinstated.");
                employee.CurrentEmploymentStatus = employee.SuspendedFromStatus;
                employee.SuspendedFromStatus = null;
                break;
            case "Exited":
                throw new InvalidOperationException(
                    "Employee exits must use the approved exit and clearance workflow.");
            default:
                throw new ArgumentException("Lifecycle event type is not a supported transition.");
        }

        employee.Version = checked(employee.Version + 1);
        var currentState = HrEmployeeHistoryJson.State(
            employee.CurrentEmploymentStatus, employee.CurrentEmploymentTypeId,
            employee.CurrentPositionId, employee.CurrentOrgUnitId, previousState.ReportsToEmployeeId);
        var eventRow = NewLifecycleEvent(businessId, employee, period, actorUserId,
            data.EventType, data.EffectiveDate, previousState.Json, currentState, data.Reason);
        db.HrEmployeeLifecycleEvents.Add(eventRow);
        await SaveHistoryChangesAsync(cancellationToken);
        return Map(eventRow);
    }

    public async Task<HrEmployeeLifecycleEventReadModel?> RehireAsync(
        long businessId,
        long actorUserId,
        long employeeId,
        long expectedVersion,
        HrEmployeeRehireData data,
        CancellationToken cancellationToken)
    {
        await LockHistoryScopeAsync(businessId, cancellationToken);
        var employee = await db.HrEmployees.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == employeeId, cancellationToken);
        if (employee is null)
            return null;
        RequireExpectedVersion(employee, expectedVersion);
        if (employee.CurrentEmploymentStatus != "Exited")
            throw new InvalidOperationException("Only an exited employee can be rehired.");
        if (await db.HrEmploymentPeriods.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == employeeId && row.Status == "Active",
                cancellationToken))
            throw new InvalidOperationException("Employee already has an active employment period.");
        if (!await db.HrEmploymentTypes.AnyAsync(row =>
                row.BusinessId == businessId && row.Id == data.EmploymentTypeId && row.IsActive,
                cancellationToken))
            throw new InvalidOperationException("Employment type was not found or is inactive in this Business.");

        var previousPeriod = await db.HrEmploymentPeriods
            .Where(row => row.BusinessId == businessId && row.EmployeeId == employeeId)
            .OrderByDescending(row => row.StartDate).ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Exited employee has no prior employment period.");
        if (!previousPeriod.EndDate.HasValue || data.EffectiveDate <= previousPeriod.EndDate.Value)
            throw new InvalidOperationException("Rehire must start after the prior employment period ends.");
        ValidateEffectiveDate(data.EffectiveDate, previousPeriod.EndDate.Value.AddDays(1));

        var previousState = HrEmployeeHistoryJson.State(
            employee.CurrentEmploymentStatus, employee.CurrentEmploymentTypeId,
            employee.CurrentPositionId, employee.CurrentOrgUnitId, null);
        var period = new HrEmploymentPeriod
        {
            BusinessId = businessId,
            Employee = employee,
            EmploymentTypeId = data.EmploymentTypeId,
            StartDate = data.EffectiveDate,
            StartReason = Normalize(data.Reason),
            Status = "Active",
            SourceEventType = "Rehired"
        };
        employee.CurrentEmploymentStatus = data.EmploymentStatus;
        employee.CurrentEmploymentTypeId = data.EmploymentTypeId;
        employee.CurrentPositionId = null;
        employee.CurrentOrgUnitId = null;
        employee.CurrentPrimaryLocationProjectionId = null;
        employee.ConfirmationDate = null;
        employee.LastWorkingDate = null;
        employee.SuspendedFromStatus = null;
        employee.Version = checked(employee.Version + 1);
        db.HrEmploymentPeriods.Add(period);
        var eventRow = NewLifecycleEvent(businessId, employee, period, actorUserId,
            "Rehired", data.EffectiveDate, previousState,
            HrEmployeeHistoryJson.State(data.EmploymentStatus, data.EmploymentTypeId, null, null, null),
            data.Reason);
        db.HrEmployeeLifecycleEvents.Add(eventRow);
        await SaveHistoryChangesAsync(cancellationToken);
        return Map(eventRow);
    }

    public async Task<HrEmployeePositionAssignmentReadModel?> AssignPositionAsync(
        long businessId,
        long actorUserId,
        long employeeId,
        long expectedVersion,
        HrEmployeePositionAssignmentData data,
        CancellationToken cancellationToken)
    {
        await LockHistoryScopeAsync(businessId, cancellationToken);
        var employee = await db.HrEmployees.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == employeeId, cancellationToken);
        if (employee is null)
            return null;
        RequireExpectedVersion(employee, expectedVersion);
        if (employee.CurrentEmploymentStatus is "Exited" or "Suspended")
            throw new InvalidOperationException("Exited or suspended employees cannot receive a position assignment.");
        var period = await db.HrEmploymentPeriods.SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.EmployeeId == employeeId && row.Status == "Active",
            cancellationToken)
            ?? throw new InvalidOperationException("Employee has no active employment period.");
        ValidateEffectiveDate(data.EffectiveFrom, period.StartDate);

        var type = Enum.Parse<HrEmployeeAssignmentType>(data.AssignmentType, true);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (type == HrEmployeeAssignmentType.Primary && data.EffectiveTo.HasValue)
            throw new ArgumentException("A primary assignment remains open until replaced or employment ends.");

        var position = await db.HrPositions.AsNoTracking().SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == data.PositionId, cancellationToken)
            ?? throw new InvalidOperationException("Position was not found in this Business.");
        if (position.Status == HrPositionStatus.Inactive)
            throw new InvalidOperationException("Position is inactive.");
        if (position.OrgUnitId != data.OrgUnitId)
            throw new InvalidOperationException("Assignment Department / Team must match the selected position.");
        var orgUnit = await db.HrOrgUnits.AsNoTracking().SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == data.OrgUnitId && row.IsActive,
            cancellationToken)
            ?? throw new InvalidOperationException("Department / Team was not found or is inactive in this Business.");
        if (orgUnit.EffectiveFrom > data.EffectiveFrom
            || (orgUnit.EffectiveTo.HasValue && orgUnit.EffectiveTo.Value < data.EffectiveFrom))
            throw new InvalidOperationException("Department / Team is not effective on the assignment date.");

        if (data.ReportsToEmployeeId == employeeId)
            throw new InvalidOperationException("An employee cannot report to themselves.");
        if (data.ReportsToEmployeeId.HasValue)
        {
            var manager = await db.HrEmployees.AsNoTracking().SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == data.ReportsToEmployeeId.Value,
                cancellationToken)
                ?? throw new InvalidOperationException("Reports-to employee was not found in this Business.");
            if (manager.CurrentEmploymentStatus is "Exited" or "Suspended")
                throw new InvalidOperationException("Reports-to employee is not currently active.");
            await EnsureNoManagerCycleAsync(
                businessId, employeeId, data.ReportsToEmployeeId.Value, data.EffectiveFrom,
                cancellationToken);
        }

        var currentPrimary = await db.HrEmployeePositionAssignments.SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.EmployeeId == employeeId
            && row.AssignmentType == HrEmployeeAssignmentType.Primary && row.IsCurrent,
            cancellationToken);
        var existingPrimary = type == HrEmployeeAssignmentType.Primary ? currentPrimary : null;
        if (existingPrimary is not null)
        {
            if (data.EffectiveFrom <= existingPrimary.EffectiveFrom)
                throw new InvalidOperationException("A primary position change must be later than the current assignment start.");
            existingPrimary.EffectiveTo = data.EffectiveFrom.AddDays(-1);
            existingPrimary.IsCurrent = false;
            existingPrimary.Version = checked(existingPrimary.Version + 1);
        }

        var existingPrimaryId = existingPrimary?.Id ?? 0;
        if (type == HrEmployeeAssignmentType.Primary
            && await db.HrEmployeePositionAssignments.AnyAsync(row =>
                row.BusinessId == businessId && row.EmployeeId == employeeId
                && row.AssignmentType == HrEmployeeAssignmentType.Primary
                && row.Id != existingPrimaryId
                && row.EffectiveFrom <= (data.EffectiveTo ?? DateOnly.MaxValue)
                && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= data.EffectiveFrom),
                cancellationToken))
            throw new InvalidOperationException("Primary position assignment overlaps an existing assignment.");

        var oldPositionId = employee.CurrentPositionId;
        var oldOrgUnitId = employee.CurrentOrgUnitId;
        var oldManagerId = currentPrimary?.ReportsToEmployeeId;
        var previousState = HrEmployeeHistoryJson.State(
            employee.CurrentEmploymentStatus, employee.CurrentEmploymentTypeId,
            oldPositionId, oldOrgUnitId, oldManagerId);
        var assignment = new HrEmployeePositionAssignment
        {
            BusinessId = businessId,
            EmployeeId = employeeId,
            PositionId = data.PositionId,
            OrgUnitId = data.OrgUnitId,
            ReportsToEmployeeId = data.ReportsToEmployeeId,
            AssignmentType = type,
            EffectiveFrom = data.EffectiveFrom,
            EffectiveTo = data.EffectiveTo,
            Reason = Normalize(data.Reason),
            IsCurrent = data.EffectiveFrom <= today
                && (!data.EffectiveTo.HasValue || data.EffectiveTo.Value >= today),
            Version = 1
        };
        db.HrEmployeePositionAssignments.Add(assignment);
        if (type == HrEmployeeAssignmentType.Primary)
        {
            employee.CurrentPositionId = assignment.PositionId;
            employee.CurrentOrgUnitId = assignment.OrgUnitId;
        }
        employee.Version = checked(employee.Version + 1);

        var newState = HrEmployeeHistoryJson.State(
            employee.CurrentEmploymentStatus, employee.CurrentEmploymentTypeId,
            assignment.PositionId, assignment.OrgUnitId, assignment.ReportsToEmployeeId);
        var eventTypes = new List<string>();
        if (oldPositionId != assignment.PositionId)
            eventTypes.Add("PositionChanged");
        if (oldOrgUnitId != assignment.OrgUnitId)
            eventTypes.Add("OrgUnitChanged");
        if (oldManagerId != assignment.ReportsToEmployeeId)
            eventTypes.Add("ManagerChanged");
        if (eventTypes.Count == 0)
        {
            if (type == HrEmployeeAssignmentType.Primary)
                throw new InvalidOperationException("The employee already has this position and reporting assignment.");
            eventTypes.Add("PositionChanged");
        }
        foreach (var eventType in eventTypes)
        {
            db.HrEmployeeLifecycleEvents.Add(NewLifecycleEvent(
                businessId, employee, period, actorUserId, eventType,
                data.EffectiveFrom, previousState, newState, data.Reason));
        }

        await SaveHistoryChangesAsync(cancellationToken);
        var assignments = await GetPositionHistoryAsync(businessId, employeeId, cancellationToken);
        return assignments.FirstOrDefault(row => row.Id == assignment.Id);
    }

    private async Task<(string Json, long? ReportsToEmployeeId)> CurrentStateAsync(
        HrEmployee employee, DateOnly effectiveDate, CancellationToken cancellationToken)
    {
        var assignment = await db.HrEmployeePositionAssignments.AsNoTracking()
            .Where(row => row.BusinessId == employee.BusinessId && row.EmployeeId == employee.Id
                && row.AssignmentType == HrEmployeeAssignmentType.Primary
                && row.EffectiveFrom <= effectiveDate
                && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= effectiveDate))
            .OrderByDescending(row => row.EffectiveFrom).ThenByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return (HrEmployeeHistoryJson.State(
            employee.CurrentEmploymentStatus, employee.CurrentEmploymentTypeId,
            employee.CurrentPositionId, employee.CurrentOrgUnitId, assignment?.ReportsToEmployeeId),
            assignment?.ReportsToEmployeeId);
    }

    private async Task EnsureNoManagerCycleAsync(
        long businessId,
        long employeeId,
        long managerId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken)
    {
        var edges = await db.HrEmployeePositionAssignments.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.ReportsToEmployeeId.HasValue
                && row.EffectiveFrom <= effectiveDate
                && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= effectiveDate))
            .Select(row => new { row.EmployeeId, ManagerId = row.ReportsToEmployeeId.GetValueOrDefault() })
            .ToListAsync(cancellationToken);
        var graph = edges.GroupBy(row => row.EmployeeId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.ManagerId).ToList());
        if (!graph.TryGetValue(employeeId, out var reportsTo))
        {
            reportsTo = [];
            graph.Add(employeeId, reportsTo);
        }
        reportsTo.Add(managerId);

        var pending = new Stack<long>();
        pending.Push(managerId);
        var visited = new HashSet<long>();
        while (pending.TryPop(out var current))
        {
            if (current == employeeId)
                throw new InvalidOperationException("Employee reporting hierarchy cannot contain a cycle.");
            if (!visited.Add(current) || !graph.TryGetValue(current, out var managers))
                continue;
            foreach (var next in managers)
                pending.Push(next);
        }
    }

    private async Task LockHistoryScopeAsync(long businessId, CancellationToken cancellationToken)
    {
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            if (db.Database.CurrentTransaction is null)
                throw new InvalidOperationException("Employee history writes require the Audit v2 transaction.");
            var lockKey = $"hr-employee-history:{businessId}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
        }
    }

    private async Task SaveHistoryChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new HrEmployeeConcurrencyException("Employee or assignment changed by another request.", exception);
        }
    }

    private static void RequireExpectedVersion(HrEmployee employee, long expectedVersion)
    {
        if (employee.Version != expectedVersion)
            throw new HrEmployeeConcurrencyException("Employee changed since it was read.");
    }

    private static void ValidateEffectiveDate(DateOnly effectiveDate, DateOnly periodStart)
    {
        if (effectiveDate == default || effectiveDate < periodStart)
            throw new ArgumentException("Effective date must not precede the employment period start.");
        if (effectiveDate > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("Future-effective employee history changes are not supported.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static HrEmployeeLifecycleEvent NewLifecycleEvent(
        long businessId,
        HrEmployee employee,
        HrEmploymentPeriod period,
        long actorUserId,
        string eventType,
        DateOnly effectiveDate,
        string previousState,
        string newState,
        string? reason) =>
        new()
        {
            BusinessId = businessId,
            Employee = employee,
            EmploymentPeriod = period,
            EventType = eventType,
            EffectiveDate = effectiveDate,
            PreviousStateJson = previousState,
            NewStateJson = newState,
            Reason = Normalize(reason),
            ActorUserId = actorUserId,
            OccurredAtUtc = DateTime.UtcNow
        };

    private static HrEmployeeLifecycleEventReadModel Map(HrEmployeeLifecycleEvent row) =>
        new(row.Id, row.EmployeeId, row.EmploymentPeriodId, row.EventType, row.EffectiveDate,
            row.PreviousStateJson, row.NewStateJson, row.Reason, row.ActorUserId,
            new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc)));
}

internal static class HrEmployeeHistoryJson
{
    public static string State(
        string employmentStatus,
        long employmentTypeId,
        long? positionId,
        long? orgUnitId,
        long? reportsToEmployeeId) =>
        JsonSerializer.Serialize(new
        {
            employmentStatus,
            employmentTypeId,
            positionId,
            orgUnitId,
            reportsToEmployeeId
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
