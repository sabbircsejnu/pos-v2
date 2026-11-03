using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Reports;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Reports;

public sealed partial class HrCoreReportStore(RetailPOSDbContext db) : IHrCoreReportStore
{
    public async Task<HrCoreReportPage> QueryAsync(
        long businessId,
        HrCoreReportQuery request,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.BusinessId != businessId)
            throw new UnauthorizedAccessException("Core HR report scope is outside Business.");

        var allowedLocationIds = await ResolveLocationIdsAsync(businessId, scope, cancellationToken);
        if (request.Kind == "LeaveBalance")
            return await QueryLeaveBalanceAsync(businessId, request, allowedLocationIds, cancellationToken);
        if (request.Kind == "DocumentExpiry")
            return await QueryDocumentExpiryAsync(businessId, request, allowedLocationIds, cancellationToken);
        if (request.Kind == "AttendanceSummary")
            return await AttendanceSummaryQuery(businessId, request, allowedLocationIds, cancellationToken);

        var query = request.Kind switch
        {
            "EmployeeMaster" or "EmployeeRoster" =>
                EmployeeRosterQuery(businessId, request, allowedLocationIds),
            "Lifecycle" or "LifecycleHistory" =>
                LifecycleHistoryQuery(businessId, request, allowedLocationIds),
            "PositionAssignmentHistory" =>
                PositionAssignmentHistoryQuery(businessId, request, allowedLocationIds),
            "LocationAssignmentHistory" =>
                LocationAssignmentHistoryQuery(businessId, request, allowedLocationIds),
            "AttendanceExceptions" =>
                AttendanceExceptionsQuery(businessId, request, allowedLocationIds),
            "AttendanceCorrections" =>
                AttendanceCorrectionsQuery(businessId, request, allowedLocationIds),
            "LeaveSummary" =>
                LeaveSummaryQuery(businessId, request, allowedLocationIds),
            "OvertimeSummary" =>
                OvertimeSummaryQuery(businessId, request, allowedLocationIds),
            "ExitClearance" => ExitClearanceQuery(businessId, request, allowedLocationIds),
            _ => throw new ArgumentException("Unknown core HR report kind.", nameof(request))
        };

        if (!string.IsNullOrWhiteSpace(request.Status))
            query = query.Where(row => row.Status == request.Status);

        var totalCount = await query.LongCountAsync(cancellationToken);
        List<HrCoreReportRow> items;
        if (request.SortBy is null)
        {
            items = await query.Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize).ToListAsync(cancellationToken);
        }
        else
        {
            if (totalCount > HrCoreReportLimits.MaximumExportRows)
                throw new ArgumentException(
                    $"Sorting is limited to {HrCoreReportLimits.MaximumExportRows} matching report rows.");
            var sortedRows = ApplySafeSort(
                await query.ToListAsync(cancellationToken), request);
            items = sortedRows.Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize).ToList();
        }

        return new HrCoreReportPage(request.Kind, items, request.Page, request.PageSize, totalCount);
    }

    private IQueryable<HrCoreReportRow> EmployeeRosterQuery(
        long businessId, HrCoreReportQuery request, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var employees = ApplyEmployeeFilters(VisibleEmployees(businessId, allowedLocationIds), request)
            .Where(employee => employee.JoiningDate >= request.From && employee.JoiningDate <= request.To);
        if (request.EmployeeId is long employeeId)
            employees = employees.Where(employee => employee.Id == employeeId);
        if (request.LocationProjectionId is long locationId)
            employees = employees.Where(employee => employee.CurrentPrimaryLocationProjectionId == locationId);

        return from employee in employees
                   .OrderByDescending(employee => employee.JoiningDate)
                       .ThenBy(employee => employee.EmployeeCode)
                       .ThenBy(employee => employee.Id)
                   select new HrCoreReportRow(
                       employee.Id,
                   employee.EmployeeCode,
                   employee.DisplayName,
                   "Employee",
                   employee.JoiningDate,
                   employee.CurrentEmploymentStatus,
                   db.OperationalLocationProjections.AsNoTracking()
                       .Where(location => location.BusinessId == businessId
                           && location.Id == employee.CurrentPrimaryLocationProjectionId)
                       .Select(location => location.Name).FirstOrDefault(),
                   null,
                   null,
                   db.HrEmploymentTypes.AsNoTracking()
                       .Where(type => type.BusinessId == businessId
                           && type.Id == employee.CurrentEmploymentTypeId)
                       .Select(type => type.Name).FirstOrDefault(),
                   db.HrOrgUnits.AsNoTracking()
                       .Where(unit => unit.BusinessId == businessId
                           && unit.Id == employee.CurrentOrgUnitId)
                       .Select(unit => unit.Code + " — " + unit.Name).FirstOrDefault(),
                   db.HrPositions.AsNoTracking()
                       .Where(position => position.BusinessId == businessId
                           && position.Id == employee.CurrentPositionId)
                       .Select(position => position.Code + " — " + position.Name).FirstOrDefault(),
                   db.OperationalLocationProjections.AsNoTracking()
                       .Where(location => location.BusinessId == businessId
                           && location.Id == employee.CurrentPrimaryLocationProjectionId)
                       .Select(location => location.SourceEntityType).FirstOrDefault());
    }

    private IQueryable<HrCoreReportRow> LifecycleQuery(
        long businessId, HrCoreReportQuery request, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var events = from history in db.HrEmployeeLifecycleEvents.AsNoTracking()
                     join employee in VisibleEmployees(businessId, allowedLocationIds)
                         on new { history.BusinessId, history.EmployeeId }
                         equals new { employee.BusinessId, EmployeeId = employee.Id }
                     where history.BusinessId == businessId
                         && history.EffectiveDate >= request.From && history.EffectiveDate <= request.To
                         && (request.EmployeeId == null || history.EmployeeId == request.EmployeeId)
                         && (request.LocationProjectionId == null
                             || employee.CurrentPrimaryLocationProjectionId == request.LocationProjectionId)
                     orderby history.EffectiveDate descending, employee.EmployeeCode, employee.Id
                     select new HrCoreReportRow(
                         employee.Id, employee.EmployeeCode, employee.DisplayName, "Lifecycle",
                         history.EffectiveDate, history.EventType,
                         db.OperationalLocationProjections.AsNoTracking()
                             .Where(location => location.BusinessId == businessId
                                 && location.Id == employee.CurrentPrimaryLocationProjectionId)
                             .Select(location => location.Name).FirstOrDefault(),
                         null, null);
        return events;
    }

    private IQueryable<HrCoreReportRow> ExitClearanceQuery(
        long businessId, HrCoreReportQuery request, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var cases = from exitCase in db.HrExitCases.AsNoTracking()
                    join employee in VisibleEmployees(businessId, allowedLocationIds)
                        on new { exitCase.BusinessId, exitCase.EmployeeId }
                        equals new { employee.BusinessId, EmployeeId = employee.Id }
                    where exitCase.BusinessId == businessId
                        && exitCase.InitiatedDate >= request.From && exitCase.InitiatedDate <= request.To
                        && (request.EmployeeId == null || exitCase.EmployeeId == request.EmployeeId)
                        && (request.LocationProjectionId == null
                            || exitCase.PrimaryLocationProjectionIdSnapshot == request.LocationProjectionId)
                    orderby exitCase.InitiatedDate descending, employee.EmployeeCode, employee.Id
                    select new HrCoreReportRow(
                        employee.Id, employee.EmployeeCode, employee.DisplayName, "ExitClearance",
                        exitCase.InitiatedDate, exitCase.Status,
                        db.OperationalLocationProjections.AsNoTracking()
                            .Where(location => location.BusinessId == businessId
                                && location.Id == exitCase.PrimaryLocationProjectionIdSnapshot)
                            .Select(location => location.Name).FirstOrDefault(),
                        exitCase.ExitType,
                        db.HrClearanceItems.AsNoTracking().Count(item =>
                            item.BusinessId == businessId && item.ExitCaseId == exitCase.Id
                            && item.IsMandatory && item.Status == "Pending"),
                        null, null, null, null,
                        exitCase.ActualLastWorkingDate ?? exitCase.ProposedLastWorkingDate,
                        exitCase.CompletionEvidence == null,
                        null, null, null,
                        exitCase.CompletionEvidence == null
                            ? null : exitCase.CompletionEvidence.CompletionMode,
                        exitCase.CompletionEvidence == null
                            ? null : exitCase.CompletionEvidence.PayrollEnabled,
                        new HrCoreReportMeasures(
                            null, null, null, null, null, null, null, null, null,
                            null, null, null, null, null, null, null, null,
                            db.HrClearanceItems.AsNoTracking().Count(item =>
                                item.BusinessId == businessId && item.ExitCaseId == exitCase.Id && item.IsMandatory),
                            db.HrClearanceItems.AsNoTracking().Count(item =>
                                item.BusinessId == businessId && item.ExitCaseId == exitCase.Id
                                && item.IsMandatory && item.Status == "Cleared")));
        return cases;
    }

    private IQueryable<HrEmployee> VisibleEmployees(
        long businessId, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var query = db.HrEmployees.AsNoTracking().Where(employee => employee.BusinessId == businessId);
        if (allowedLocationIds is not null)
            query = query.Where(employee => employee.CurrentPrimaryLocationProjectionId.HasValue
                && allowedLocationIds.Contains(employee.CurrentPrimaryLocationProjectionId.Value));
        return query;
    }

    private async Task<IReadOnlyCollection<long>?> ResolveLocationIdsAsync(
        long businessId, HrOperationalLocationReadScope scope, CancellationToken cancellationToken)
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
