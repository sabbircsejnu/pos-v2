using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Dashboard;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Dashboard;

public sealed class HrDashboardStore(RetailPOSDbContext db) : IHrDashboardStore
{
    public async Task<HrDashboardSummary> QueryAsync(
        long businessId,
        HrDashboardQuery request,
        HrOperationalLocationReadScope scope,
        bool attendanceEnabled,
        CancellationToken cancellationToken)
    {
        if (scope.BusinessId != businessId)
            throw new UnauthorizedAccessException("HR dashboard scope is outside Business.");

        var allowedLocationIds = await ResolveLocationIdsAsync(businessId, scope, cancellationToken);
        if (request.LocationProjectionId is long locationId
            && allowedLocationIds is not null && !allowedLocationIds.Contains(locationId))
            throw new UnauthorizedAccessException("The dashboard location is outside the caller's scope.");

        IReadOnlyCollection<long>? queryLocationIds = allowedLocationIds;
        if (request.LocationType is { } locationType)
        {
            var locationTypeIds = await db.OperationalLocationProjections.AsNoTracking()
                .Where(location => location.BusinessId == businessId
                    && location.SourceEntityType == locationType)
                .Select(location => location.Id)
                .ToListAsync(cancellationToken);
            queryLocationIds = allowedLocationIds is null
                ? locationTypeIds
                : allowedLocationIds.Intersect(locationTypeIds).ToArray();
        }

        var employees = db.HrEmployees.AsNoTracking().Where(employee =>
            employee.BusinessId == businessId
            && (request.LocationProjectionId == null
                || employee.CurrentPrimaryLocationProjectionId == request.LocationProjectionId)
            && (queryLocationIds == null
                || queryLocationIds.Contains(employee.CurrentPrimaryLocationProjectionId!.Value)));

        var totalEmployees = await employees.LongCountAsync(cancellationToken);
        var employmentStatusRows = await employees
            .GroupBy(employee => employee.CurrentEmploymentStatus)
            .Select(group => new { Label = group.Key, Count = group.LongCount() })
            .OrderBy(row => row.Label)
            .ToListAsync(cancellationToken);
        var employmentTypeRows = await (
                from employee in employees
                join type in db.HrEmploymentTypes.AsNoTracking()
                    on new { employee.BusinessId, Id = employee.CurrentEmploymentTypeId }
                    equals new { type.BusinessId, type.Id }
                group employee by type.Name into g
                select new { Label = g.Key, Count = g.LongCount() })
            .OrderBy(row => row.Label)
            .ToListAsync(cancellationToken);

        var locationTypeRows = await (
                from employee in employees
                join location in db.OperationalLocationProjections.AsNoTracking()
                    on new { employee.BusinessId, Id = employee.CurrentPrimaryLocationProjectionId }
                    equals new { location.BusinessId, Id = (long?)location.Id }
                group employee by location.SourceEntityType into g
                select new { Label = g.Key, Count = g.LongCount() })
            .OrderByDescending(row => row.Count).ThenBy(row => row.Label)
            .ToListAsync(cancellationToken);
        var locationRows = await (
                from employee in employees
                join location in db.OperationalLocationProjections.AsNoTracking()
                    on new { employee.BusinessId, Id = employee.CurrentPrimaryLocationProjectionId }
                    equals new { location.BusinessId, Id = (long?)location.Id }
                group employee by new { location.SourceEntityType, location.Name } into g
                select new
                {
                    g.Key.SourceEntityType,
                    g.Key.Name,
                    Count = g.LongCount()
                })
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.SourceEntityType).ThenBy(row => row.Name)
            .ToListAsync(cancellationToken);
        var orgUnitRows = await (
                from employee in employees
                join unit in db.HrOrgUnits.AsNoTracking()
                    on new { employee.BusinessId, Id = employee.CurrentOrgUnitId }
                    equals new { unit.BusinessId, Id = (long?)unit.Id }
                group employee by unit.Name into g
                select new { Label = g.Key, Count = g.LongCount() })
            .OrderByDescending(row => row.Count).ThenBy(row => row.Label)
            .ToListAsync(cancellationToken);
        var positionRows = await (
                from employee in employees
                join position in db.HrPositions.AsNoTracking()
                    on new { employee.BusinessId, Id = employee.CurrentPositionId }
                    equals new { position.BusinessId, Id = (long?)position.Id }
                group employee by position.Name into g
                select new { Label = g.Key, Count = g.LongCount() })
            .OrderByDescending(row => row.Count).ThenBy(row => row.Label)
            .ToListAsync(cancellationToken);
        var employmentStatuses = employmentStatusRows.Select(row =>
            new HrDashboardMetric(row.Label, row.Count)).ToArray();
        var employmentTypes = employmentTypeRows.Select(row =>
            new HrDashboardMetric(row.Label, row.Count)).ToArray();
        var locationTypes = locationTypeRows.Select(row =>
            new HrDashboardMetric(row.Label, row.Count)).ToArray();
        var locations = locationRows.Select(row =>
            new HrDashboardMetric($"{row.SourceEntityType}: {row.Name}", row.Count)).ToArray();
        var orgUnits = orgUnitRows.Select(row => new HrDashboardMetric(row.Label, row.Count)).ToArray();
        var positions = positionRows.Select(row => new HrDashboardMetric(row.Label, row.Count)).ToArray();

        var joiners = await employees.LongCountAsync(employee =>
            employee.JoiningDate >= request.From && employee.JoiningDate <= request.To,
            cancellationToken);
        var exits = await employees.LongCountAsync(employee =>
            employee.LastWorkingDate >= request.From && employee.LastWorkingDate <= request.To,
            cancellationToken);

        var attendance = attendanceEnabled
            ? await QueryAttendanceAsync(businessId, request, queryLocationIds, cancellationToken)
            : [];
        var leaveRequests = await QueryLeaveRequestsAsync(
            businessId, request, queryLocationIds, cancellationToken);
        var overtime = attendanceEnabled
            ? await QueryOvertimeAsync(businessId, request, queryLocationIds, cancellationToken)
            : [];
        var documentsExpiring = await (
                from document in db.HrEmployeeDocuments.AsNoTracking()
                join employee in employees
                    on new { document.BusinessId, document.EmployeeId }
                    equals new { employee.BusinessId, EmployeeId = employee.Id }
                where document.ExpiryDate >= request.From
                    && document.ExpiryDate <= request.To
                    && document.Status == "Active"
                select document.Id)
            .LongCountAsync(cancellationToken);
        var visibleExitCases = db.HrExitCases.AsNoTracking().Where(exit =>
            exit.BusinessId == businessId
            && (request.LocationProjectionId == null
                || exit.PrimaryLocationProjectionIdSnapshot == request.LocationProjectionId)
            && (queryLocationIds == null
                || queryLocationIds.Contains(exit.PrimaryLocationProjectionIdSnapshot!.Value)));
        var openExitCases = await visibleExitCases.LongCountAsync(exit =>
            exit.Status != "Completed" && exit.Status != "Cancelled" && exit.Status != "Rejected",
            cancellationToken);
        var pendingClearanceItems = await (
                from item in db.HrClearanceItems.AsNoTracking()
                join exit in visibleExitCases on new { item.BusinessId, Id = item.ExitCaseId }
                    equals new { exit.BusinessId, Id = exit.Id }
                where item.IsMandatory && item.Status != "Cleared"
                select item.Id)
            .LongCountAsync(cancellationToken);

        return new HrDashboardSummary(
            request.From, request.To, totalEmployees, employmentStatuses, employmentTypes,
            locationTypes, locations, orgUnits, positions, joiners, exits, attendance, leaveRequests,
            overtime, documentsExpiring, openExitCases, pendingClearanceItems);
    }

    private async Task<IReadOnlyList<HrDashboardMetric>> QueryAttendanceAsync(
        long businessId,
        HrDashboardQuery request,
        IReadOnlyCollection<long>? allowedLocationIds,
        CancellationToken cancellationToken)
    {
        var query = db.Set<HrDailyAttendance>().AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.WorkDate >= request.From && row.WorkDate <= request.To
                && (request.LocationProjectionId == null
                    || row.OperationalLocationProjectionId == request.LocationProjectionId));
        if (allowedLocationIds is not null)
            query = query.Where(row => allowedLocationIds.Contains(row.OperationalLocationProjectionId));

        var rows = await query.GroupBy(row => row.Status)
            .Select(group => new
            {
                Label = group.Key,
                Count = group.LongCount(),
                Minutes = group.Sum(row => (long)row.LateMinutes),
                LateCount = group.LongCount(row => row.LateMinutes > 0)
            })
            .OrderBy(row => row.Label)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new HrDashboardMetric(row.Label, row.Count, row.Minutes))
            .Append(new HrDashboardMetric("Late (records)", rows.Sum(row => row.LateCount),
                rows.Sum(row => row.Minutes)))
            .ToArray();
    }

    private async Task<IReadOnlyList<HrDashboardMetric>> QueryLeaveRequestsAsync(
        long businessId,
        HrDashboardQuery request,
        IReadOnlyCollection<long>? allowedLocationIds,
        CancellationToken cancellationToken)
    {
        var query = db.HrLeaveRequests.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.StartDate <= request.To && row.EndDate >= request.From
                && (request.LocationProjectionId == null
                    || row.ScopeLocationProjectionId == request.LocationProjectionId));
        if (allowedLocationIds is not null)
            query = query.Where(row => allowedLocationIds.Contains(row.ScopeLocationProjectionId));

        var rows = await query.GroupBy(row => row.Status)
            .Select(group => new { Label = group.Key, Count = group.LongCount() })
            .OrderBy(row => row.Label)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new HrDashboardMetric(row.Label, row.Count)).ToArray();
    }

    private async Task<IReadOnlyList<HrDashboardMetric>> QueryOvertimeAsync(
        long businessId,
        HrDashboardQuery request,
        IReadOnlyCollection<long>? allowedLocationIds,
        CancellationToken cancellationToken)
    {
        var query = db.HrOvertimeEntries.AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.WorkDate >= request.From && row.WorkDate <= request.To
                && (request.LocationProjectionId == null
                    || row.OperationalLocationProjectionId == request.LocationProjectionId));
        if (allowedLocationIds is not null)
            query = query.Where(row => allowedLocationIds.Contains(row.OperationalLocationProjectionId));

        var rows = await query.GroupBy(row => row.Status)
            .Select(group => new
            {
                Label = group.Key,
                Count = group.LongCount(),
                Minutes = group.Sum(row => (long)(row.ApprovedMinutes ?? row.CalculatedMinutes))
            })
            .OrderBy(row => row.Label)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new HrDashboardMetric(row.Label, row.Count, row.Minutes)).ToArray();
    }

    private async Task<IReadOnlyCollection<long>?> ResolveLocationIdsAsync(
        long businessId,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.AllowedSources is null)
            return null;

        var allowedSources = scope.AllowedSources
            .Select(source => source.EntityType + "\u001f" + source.EntityId)
            .ToHashSet(StringComparer.Ordinal);
        return (await db.OperationalLocationProjections.AsNoTracking()
                .Where(location => location.BusinessId == businessId)
                .Select(location => new
                {
                    location.Id,
                    location.SourceEntityType,
                    location.SourceEntityId
                }).ToListAsync(cancellationToken))
            .Where(location => allowedSources.Contains(
                location.SourceEntityType + "\u001f" + location.SourceEntityId))
            .Select(location => location.Id)
            .ToArray();
    }
}
