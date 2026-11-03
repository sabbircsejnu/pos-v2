using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Reports;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Core.Entities.HR.Overtime;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Reports;

public sealed partial class HrCoreReportStore
{
    private IQueryable<HrCoreReportRow> LifecycleHistoryQuery(
        long businessId, HrCoreReportQuery request, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var employees = ApplyEmployeeFilters(VisibleEmployees(businessId, allowedLocationIds), request);
        var events =
            from history in db.HrEmployeeLifecycleEvents.AsNoTracking()
            join employee in employees
                on new { history.BusinessId, history.EmployeeId }
                equals new { employee.BusinessId, EmployeeId = employee.Id }
            where history.BusinessId == businessId
                && history.EffectiveDate >= request.From && history.EffectiveDate <= request.To
                && (request.EmployeeId == null || history.EmployeeId == request.EmployeeId)
                && (request.LocationProjectionId == null
                    || employee.CurrentPrimaryLocationProjectionId == request.LocationProjectionId)
            select new HrCoreReportRow(
                employee.Id, employee.EmployeeCode, employee.DisplayName, history.EventType,
                history.EffectiveDate, history.EventType, null, history.Reason, null,
                null, null, null, null, null, null, history.ActorUserId,
                history.OccurredAtUtc);

        if (request.EmployeeId is long employeeId)
            events = events.Where(row => row.EmployeeId == employeeId);
        return events;
    }

    private IQueryable<HrCoreReportRow> PositionAssignmentHistoryQuery(
        long businessId, HrCoreReportQuery request, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var employees = ApplyEmployeeFilters(VisibleEmployees(businessId, allowedLocationIds), request);
        if (request.LocationProjectionId is long locationId)
            employees = employees.Where(employee =>
                employee.CurrentPrimaryLocationProjectionId == locationId);
        var assignments =
            from assignment in db.HrEmployeePositionAssignments.AsNoTracking()
            join employee in employees
                on new { assignment.BusinessId, assignment.EmployeeId }
                equals new { employee.BusinessId, EmployeeId = employee.Id }
            join position in db.HrPositions.AsNoTracking()
                on new { assignment.BusinessId, Id = assignment.PositionId }
                equals new { position.BusinessId, position.Id }
            join unit in db.HrOrgUnits.AsNoTracking()
                on new { assignment.BusinessId, Id = assignment.OrgUnitId }
                equals new { unit.BusinessId, unit.Id }
            where assignment.BusinessId == businessId
                && assignment.EffectiveFrom <= request.To
                && (assignment.EffectiveTo == null || assignment.EffectiveTo >= request.From)
                && (request.EmployeeId == null || assignment.EmployeeId == request.EmployeeId)
                && (request.PositionId == null || assignment.PositionId == request.PositionId)
                && (request.DepartmentId == null || assignment.OrgUnitId == request.DepartmentId)
            orderby assignment.EffectiveFrom descending, employee.EmployeeCode, assignment.Id
            select new HrCoreReportRow(
                employee.Id, employee.EmployeeCode, employee.DisplayName, "PositionAssignment",
                assignment.EffectiveFrom,
                assignment.AssignmentType == HrEmployeeAssignmentType.Primary ? "Primary"
                    : assignment.AssignmentType == HrEmployeeAssignmentType.Temporary ? "Temporary"
                    : "Acting",
                null, assignment.Reason, null, null, unit.Code + " — " + unit.Name,
                position.Code + " — " + position.Name, null,
                assignment.EffectiveTo, assignment.IsCurrent);
        return assignments;
    }

    private IQueryable<HrCoreReportRow> LocationAssignmentHistoryQuery(
        long businessId, HrCoreReportQuery request, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var assignments =
            from assignment in db.HrEmployeeLocationAssignments.AsNoTracking()
            join employee in ApplyEmployeeFilters(VisibleEmployees(businessId, allowedLocationIds), request)
                on new { assignment.BusinessId, assignment.EmployeeId }
                equals new { employee.BusinessId, EmployeeId = employee.Id }
            where assignment.BusinessId == businessId
                && assignment.EffectiveFrom <= request.To
                && (assignment.EffectiveTo == null || assignment.EffectiveTo >= request.From)
                && (request.EmployeeId == null || assignment.EmployeeId == request.EmployeeId)
                && (request.LocationProjectionId == null
                    || assignment.OperationalLocationProjectionId == request.LocationProjectionId)
                && (allowedLocationIds == null || allowedLocationIds.Contains(
                    assignment.OperationalLocationProjectionId))
            orderby assignment.EffectiveFrom descending, employee.EmployeeCode, assignment.Id
            select new HrCoreReportRow(
                employee.Id, employee.EmployeeCode, employee.DisplayName, assignment.AssignmentType,
                assignment.EffectiveFrom, assignment.Status, assignment.LocationNameSnapshot,
                assignment.Reason, null, null, null, null,
                assignment.SourceEntityTypeSnapshot, assignment.EffectiveTo,
                assignment.EffectiveTo == null && assignment.Status == "Effective");
        return assignments;
    }

    private async Task<HrCoreReportPage> AttendanceSummaryQuery(
        long businessId,
        HrCoreReportQuery request,
        IReadOnlyCollection<long>? allowedLocationIds,
        CancellationToken cancellationToken)
    {
        var attendance = db.Set<HrDailyAttendance>().AsNoTracking()
            .Where(row => row.BusinessId == businessId
                && row.WorkDate >= request.From && row.WorkDate <= request.To
                && (request.EmployeeId == null || row.EmployeeId == request.EmployeeId)
                && (request.LocationProjectionId == null
                    || row.OperationalLocationProjectionId == request.LocationProjectionId)
                && (allowedLocationIds == null
                    || allowedLocationIds.Contains(row.OperationalLocationProjectionId)));
        if (!string.IsNullOrWhiteSpace(request.Status))
            attendance = attendance.Where(row => row.Status == request.Status);

        var rows =
            from daily in attendance
            join employee in ApplyEmployeeFilters(VisibleEmployees(businessId, allowedLocationIds), request)
                on new { daily.BusinessId, daily.EmployeeId }
                equals new { employee.BusinessId, EmployeeId = employee.Id }
            join location in db.OperationalLocationProjections.AsNoTracking()
                on new { daily.BusinessId, Id = daily.OperationalLocationProjectionId }
                equals new { location.BusinessId, location.Id }
            group daily by new
            {
                employee.Id,
                employee.EmployeeCode,
                employee.DisplayName,
                LocationName = location.Name
            } into groupRows
            select new
            {
                groupRows.Key.Id,
                groupRows.Key.EmployeeCode,
                groupRows.Key.DisplayName,
                groupRows.Key.LocationName,
                ScheduledDays = groupRows.Count(row =>
                    row.DayClassification != "Holiday" && row.DayClassification != "WeeklyOff"),
                PresentDays = groupRows.Count(row =>
                    row.Status == "Present" || row.Status == "Late"
                    || row.Status == "HalfDay" || row.Status == "MissingPunch"),
                AbsentDays = groupRows.Count(row => row.Status == "Absent"),
                LateDays = groupRows.Count(row => row.LateMinutes > 0),
                HolidayDays = groupRows.Count(row =>
                    row.DayClassification == "Holiday" || row.Status == "Holiday"),
                WeeklyOffDays = groupRows.Count(row =>
                    row.DayClassification == "WeeklyOff" || row.Status == "WeeklyOff"),
                WorkedMinutes = groupRows.Sum(row => row.WorkedMinutes)
            };

        var totalCount = await rows.LongCountAsync(cancellationToken);
        var ordered = request.SortBy switch
        {
            "employeeName" => request.Descending
                ? rows.OrderByDescending(row => row.DisplayName).ThenBy(row => row.Id)
                : rows.OrderBy(row => row.DisplayName).ThenBy(row => row.Id),
            "locationName" => request.Descending
                ? rows.OrderByDescending(row => row.LocationName).ThenBy(row => row.EmployeeCode)
                : rows.OrderBy(row => row.LocationName).ThenBy(row => row.EmployeeCode),
            _ => request.Descending
                ? rows.OrderByDescending(row => row.EmployeeCode).ThenBy(row => row.Id)
                : rows.OrderBy(row => row.EmployeeCode).ThenBy(row => row.Id)
        };
        var pageRows = await ordered.Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize).ToListAsync(cancellationToken);
        var employeeIds = pageRows.Select(row => row.Id).ToList();
        var approvedLeaveUnits = employeeIds.Count == 0
            ? new Dictionary<long, decimal>()
            : await (from day in db.HrLeaveRequestDays.AsNoTracking()
                     join leave in db.HrLeaveRequests.AsNoTracking()
                         on new { day.BusinessId, Id = day.LeaveRequestId }
                         equals new { leave.BusinessId, Id = leave.Id }
                     where day.BusinessId == businessId
                         && employeeIds.Contains(day.EmployeeId)
                         && day.WorkDate >= request.From && day.WorkDate <= request.To
                         && leave.Status == "Approved"
                         && (request.LocationProjectionId == null
                             || day.OperationalLocationProjectionId == request.LocationProjectionId)
                         && (allowedLocationIds == null || allowedLocationIds.Contains(
                             day.OperationalLocationProjectionId))
                     group day by day.EmployeeId into groupRows
                     select new { EmployeeId = groupRows.Key, Units = groupRows.Sum(day => day.Units) })
                .ToDictionaryAsync(row => row.EmployeeId, row => row.Units, cancellationToken);
        var approvedOvertimeCounts = employeeIds.Count == 0
            ? new Dictionary<long, int>()
            : await db.HrOvertimeEntries.AsNoTracking()
                .Where(row => row.BusinessId == businessId
                    && employeeIds.Contains(row.EmployeeId)
                    && row.WorkDate >= request.From && row.WorkDate <= request.To
                    && row.Status == "PayrollEligible"
                    && (request.LocationProjectionId == null
                        || row.OperationalLocationProjectionId == request.LocationProjectionId)
                    && (allowedLocationIds == null
                        || allowedLocationIds.Contains(row.OperationalLocationProjectionId)))
                .GroupBy(row => row.EmployeeId)
                .Select(groupRows => new { EmployeeId = groupRows.Key, Count = groupRows.Count() })
                .ToDictionaryAsync(row => row.EmployeeId, row => row.Count, cancellationToken);

        var items = pageRows.Select(row => new HrCoreReportRow(
            row.Id, row.EmployeeCode, row.DisplayName, "AttendanceSummary", null, "Processed",
            row.LocationName, null, null,
            Measures: new HrCoreReportMeasures(
                row.ScheduledDays, row.PresentDays, row.AbsentDays, row.LateDays,
                row.HolidayDays, row.WeeklyOffDays,
                approvedLeaveUnits.GetValueOrDefault(row.Id), row.WorkedMinutes,
                approvedOvertimeCounts.GetValueOrDefault(row.Id)))).ToArray();
        return new HrCoreReportPage(request.Kind, items, request.Page, request.PageSize, totalCount);
    }

    private IQueryable<HrCoreReportRow> AttendanceExceptionsQuery(
        long businessId, HrCoreReportQuery request, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var employees = ApplyEmployeeFilters(VisibleEmployees(businessId, allowedLocationIds), request);
        var attendance =
            from daily in db.Set<HrDailyAttendance>().AsNoTracking()
            join employee in employees
                on new { daily.BusinessId, daily.EmployeeId }
                equals new { employee.BusinessId, EmployeeId = employee.Id }
            join location in db.OperationalLocationProjections.AsNoTracking()
                on new { daily.BusinessId, Id = daily.OperationalLocationProjectionId }
                equals new { location.BusinessId, location.Id }
            where daily.BusinessId == businessId
                && daily.WorkDate >= request.From && daily.WorkDate <= request.To
                && (request.EmployeeId == null || daily.EmployeeId == request.EmployeeId)
                && (request.LocationProjectionId == null
                    || daily.OperationalLocationProjectionId == request.LocationProjectionId)
                && (allowedLocationIds == null
                    || allowedLocationIds.Contains(daily.OperationalLocationProjectionId))
                && (daily.Status == "MissingPunch"
                    || (daily.FirstInAtLocal == null) != (daily.LastOutAtLocal == null))
            select new HrCoreReportRow(
                employee.Id, employee.EmployeeCode, employee.DisplayName, "AttendanceException",
                daily.WorkDate, daily.Status, location.Name,
                daily.FirstInAtLocal == null ? "Missing first punch" : "Missing last punch",
                daily.ProcessingVersion);

        return attendance;
    }

    private IQueryable<HrCoreReportRow> AttendanceCorrectionsQuery(
        long businessId, HrCoreReportQuery request, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var employees = ApplyEmployeeFilters(VisibleEmployees(businessId, allowedLocationIds), request);
        return
            from correction in db.Set<HrAttendanceCorrectionRequest>().AsNoTracking()
            join employee in employees
                on new { correction.BusinessId, correction.EmployeeId }
                equals new { employee.BusinessId, EmployeeId = employee.Id }
            join location in db.OperationalLocationProjections.AsNoTracking()
                on new { correction.BusinessId, Id = correction.OperationalLocationProjectionId }
                equals new { location.BusinessId, location.Id }
            where correction.BusinessId == businessId
                && correction.WorkDate >= request.From && correction.WorkDate <= request.To
                && (request.EmployeeId == null || correction.EmployeeId == request.EmployeeId)
                && (request.LocationProjectionId == null
                    || correction.OperationalLocationProjectionId == request.LocationProjectionId)
                && (allowedLocationIds == null
                    || allowedLocationIds.Contains(correction.OperationalLocationProjectionId))
            select new HrCoreReportRow(
                employee.Id, employee.EmployeeCode, employee.DisplayName, "AttendanceCorrection",
                correction.WorkDate, correction.Status, location.Name,
                correction.Reason, correction.CorrectionSequence,
                null, null, null, null, null, null,
                correction.DecidedByUserId ?? correction.RequestedByUserId,
                correction.DecidedAtUtc ?? correction.RequestedAtUtc);
    }

    private IQueryable<HrCoreReportRow> LeaveSummaryQuery(
        long businessId, HrCoreReportQuery request, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var employees = ApplyEmployeeFilters(VisibleEmployees(businessId, allowedLocationIds), request);
        var leaveDays =
            from day in db.HrLeaveRequestDays.AsNoTracking()
            join leave in db.HrLeaveRequests.AsNoTracking()
                on new { day.BusinessId, Id = day.LeaveRequestId }
                equals new { leave.BusinessId, Id = leave.Id }
            join employee in employees
                on new { day.BusinessId, day.EmployeeId }
                equals new { employee.BusinessId, EmployeeId = employee.Id }
            join leaveType in db.HrLeaveTypes.AsNoTracking()
                on new { day.BusinessId, Id = leave.LeaveTypeId }
                equals new { leaveType.BusinessId, leaveType.Id }
            join location in db.OperationalLocationProjections.AsNoTracking()
                on new { day.BusinessId, Id = day.OperationalLocationProjectionId }
                equals new { location.BusinessId, location.Id }
            where day.BusinessId == businessId
                && day.WorkDate >= request.From && day.WorkDate <= request.To
                && leave.StartDate <= request.To && leave.EndDate >= request.From
                && (request.EmployeeId == null || day.EmployeeId == request.EmployeeId)
                && (request.LocationProjectionId == null
                    || day.OperationalLocationProjectionId == request.LocationProjectionId)
                && (allowedLocationIds == null
                    || allowedLocationIds.Contains(day.OperationalLocationProjectionId))
            group day by new
            {
                EmployeeId = employee.Id,
                employee.EmployeeCode,
                employee.DisplayName,
                RequestId = leave.Id,
                leave.StartDate,
                leave.EndDate,
                leave.Status,
                LeaveType = leaveType.Name,
                LocationName = location.Name
            } into groupRows
            select new HrCoreReportRow(
                groupRows.Key.EmployeeId, groupRows.Key.EmployeeCode, groupRows.Key.DisplayName,
                groupRows.Key.LeaveType, groupRows.Key.StartDate, groupRows.Key.Status,
                groupRows.Key.LocationName, null, groupRows.Count(),
                null, null, null, null, groupRows.Key.EndDate,
                null, null, null, groupRows.Sum(day => day.Units));
        return leaveDays;
    }

    private async Task<HrCoreReportPage> QueryLeaveBalanceAsync(
        long businessId,
        HrCoreReportQuery request,
        IReadOnlyCollection<long>? allowedLocationIds,
        CancellationToken cancellationToken)
    {
        var asOfDate = request.AsOfDate ?? request.To;
        HrLeaveEntitlementStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<HrLeaveEntitlementStatus>(request.Status, true, out var parsedStatus)
                || !Enum.IsDefined(parsedStatus))
                throw new ArgumentException("The leave balance status filter is invalid.", nameof(request));
            statusFilter = parsedStatus;
        }

        var entitlements =
            from entitlement in db.HrLeaveEntitlements.AsNoTracking()
            join employee in ApplyEmployeeFilters(VisibleEmployees(businessId, allowedLocationIds), request)
                on new { entitlement.BusinessId, entitlement.EmployeeId }
                equals new { employee.BusinessId, EmployeeId = employee.Id }
            join leaveType in db.HrLeaveTypes.AsNoTracking()
                on new { entitlement.BusinessId, Id = entitlement.LeaveTypeId }
                equals new { leaveType.BusinessId, leaveType.Id }
            where entitlement.BusinessId == businessId
                && entitlement.LeaveYearStart <= asOfDate
                && entitlement.LeaveYearEnd >= request.From
                && (request.EmployeeId == null || entitlement.EmployeeId == request.EmployeeId)
                && (request.LocationProjectionId == null
                    || entitlement.PrimaryLocationProjectionIdSnapshot == request.LocationProjectionId)
                && (allowedLocationIds == null
                    || entitlement.PrimaryLocationProjectionIdSnapshot.HasValue
                       && allowedLocationIds.Contains(entitlement.PrimaryLocationProjectionIdSnapshot.Value))
            select new
            {
                Entitlement = entitlement,
                employee.EmployeeCode,
                employee.DisplayName,
                LeaveType = leaveType.Name,
                leaveType.Code,
                LocationName = db.OperationalLocationProjections.AsNoTracking()
                    .Where(location => location.BusinessId == businessId
                        && location.Id == entitlement.PrimaryLocationProjectionIdSnapshot)
                    .Select(location => location.Name).FirstOrDefault()
            };
        if (statusFilter is HrLeaveEntitlementStatus entitlementStatus)
            entitlements = entitlements.Where(row => row.Entitlement.Status == entitlementStatus);
        var totalCount = await entitlements.LongCountAsync(cancellationToken);
        var ordered = request.SortBy switch
        {
            "employeeName" => request.Descending
                ? entitlements.OrderByDescending(row => row.DisplayName).ThenBy(row => row.Entitlement.Id)
                : entitlements.OrderBy(row => row.DisplayName).ThenBy(row => row.Entitlement.Id),
            "effectiveDate" => request.Descending
                ? entitlements.OrderByDescending(row => row.Entitlement.LeaveYearStart).ThenBy(row => row.Entitlement.Id)
                : entitlements.OrderBy(row => row.Entitlement.LeaveYearStart).ThenBy(row => row.Entitlement.Id),
            "category" => request.Descending
                ? entitlements.OrderByDescending(row => row.LeaveType).ThenBy(row => row.Entitlement.Id)
                : entitlements.OrderBy(row => row.LeaveType).ThenBy(row => row.Entitlement.Id),
            "status" => request.Descending
                ? entitlements.OrderByDescending(row => row.Entitlement.Status).ThenBy(row => row.Entitlement.Id)
                : entitlements.OrderBy(row => row.Entitlement.Status).ThenBy(row => row.Entitlement.Id),
            "locationName" => request.Descending
                ? entitlements.OrderByDescending(row => row.LocationName).ThenBy(row => row.Entitlement.Id)
                : entitlements.OrderBy(row => row.LocationName).ThenBy(row => row.Entitlement.Id),
            _ => entitlements.OrderBy(row => row.EmployeeCode)
                .ThenBy(row => row.Entitlement.LeaveYearStart)
                .ThenBy(row => row.Entitlement.LeaveTypeId)
        };
        var page = await ordered
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var ids = page.Select(row => row.Entitlement.Id).ToList();
        var ledger = ids.Count == 0
            ? []
            : await db.HrLeaveLedgerEntries.AsNoTracking()
                .Where(row => row.BusinessId == businessId && ids.Contains(row.EntitlementId)
                    && row.EffectiveDate <= asOfDate)
                .OrderBy(row => row.Id)
                .Select(row => new
                {
                    row.EntitlementId,
                    row.TransactionType,
                    row.Quantity
                }).ToListAsync(cancellationToken);
        var ledgerByEntitlement = ledger.GroupBy(row => row.EntitlementId)
            .ToDictionary(groupRows => groupRows.Key, groupRows => groupRows.ToArray());
        var items = page.Select(row =>
        {
            var entries = ledgerByEntitlement.GetValueOrDefault(row.Entitlement.Id) ?? [];
            decimal Sum(HrLeaveLedgerTransactionType transactionType) =>
                entries.Where(entry => entry.TransactionType == transactionType).Sum(entry => entry.Quantity);
            return new HrCoreReportRow(
                row.Entitlement.EmployeeId, row.EmployeeCode, row.DisplayName,
                row.LeaveType, row.Entitlement.LeaveYearStart,
                row.Entitlement.Status.ToString(), row.LocationName, row.Code, null,
                EffectiveTo: row.Entitlement.LeaveYearEnd,
                Quantity: entries.Sum(entry => entry.Quantity),
                Measures: new HrCoreReportMeasures(
                    Opening: Sum(HrLeaveLedgerTransactionType.Opening),
                    Accrued: Sum(HrLeaveLedgerTransactionType.Accrual),
                    Used: -Sum(HrLeaveLedgerTransactionType.Consumption),
                    Restored: Sum(HrLeaveLedgerTransactionType.Restoration),
                    Adjusted: Sum(HrLeaveLedgerTransactionType.Adjustment)
                        + Sum(HrLeaveLedgerTransactionType.CloseCorrection),
                    CarriedForward: Sum(HrLeaveLedgerTransactionType.CarryForward),
                    Expired: -Sum(HrLeaveLedgerTransactionType.Expiry),
                    Balance: entries.Sum(entry => entry.Quantity)));
        }).ToArray();
        return new HrCoreReportPage(request.Kind, items, request.Page, request.PageSize, totalCount);
    }

    private IQueryable<HrCoreReportRow> OvertimeSummaryQuery(
        long businessId, HrCoreReportQuery request, IReadOnlyCollection<long>? allowedLocationIds)
    {
        var rows =
            from overtime in db.HrOvertimeEntries.AsNoTracking()
            join employee in ApplyEmployeeFilters(VisibleEmployees(businessId, allowedLocationIds), request)
                on new { overtime.BusinessId, overtime.EmployeeId }
                equals new { employee.BusinessId, EmployeeId = employee.Id }
            join location in db.OperationalLocationProjections.AsNoTracking()
                on new { overtime.BusinessId, Id = overtime.OperationalLocationProjectionId }
                equals new { location.BusinessId, location.Id }
            where overtime.BusinessId == businessId
                && overtime.WorkDate >= request.From && overtime.WorkDate <= request.To
                && (request.EmployeeId == null || overtime.EmployeeId == request.EmployeeId)
                && (request.LocationProjectionId == null
                    || overtime.OperationalLocationProjectionId == request.LocationProjectionId)
                && (allowedLocationIds == null
                    || allowedLocationIds.Contains(overtime.OperationalLocationProjectionId))
            orderby overtime.WorkDate descending, employee.EmployeeCode, overtime.Id
            select new HrCoreReportRow(
                employee.Id, employee.EmployeeCode, employee.DisplayName, overtime.DayCategory,
                overtime.WorkDate, overtime.Status, location.Name,
                "Attendance #" + overtime.DailyAttendanceId.ToString(CultureInfo.InvariantCulture),
                overtime.ApprovedMinutes);
        return rows;
    }

    private async Task<HrCoreReportPage> QueryDocumentExpiryAsync(
        long businessId,
        HrCoreReportQuery request,
        IReadOnlyCollection<long>? allowedLocationIds,
        CancellationToken cancellationToken)
    {
        var asOfDate = request.AsOfDate ?? request.To;
        var documents =
            from document in db.HrEmployeeDocuments.AsNoTracking()
            join employee in VisibleEmployees(businessId, allowedLocationIds)
                on new { document.BusinessId, document.EmployeeId }
                equals new { employee.BusinessId, EmployeeId = employee.Id }
            join type in db.HrDocumentTypes.AsNoTracking()
                on new { document.BusinessId, document.DocumentTypeId }
                equals new { type.BusinessId, DocumentTypeId = type.Id }
            where document.BusinessId == businessId
                && document.ExpiryDate.HasValue
                && document.ExpiryDate >= request.From
                && document.ExpiryDate <= request.To
                && (request.EmployeeId == null || document.EmployeeId == request.EmployeeId)
                && (request.LocationProjectionId == null
                    || employee.CurrentPrimaryLocationProjectionId == request.LocationProjectionId)
            select new
            {
                EmployeeId = employee.Id,
                employee.EmployeeCode,
                employee.DisplayName,
                ExpiryDate = document.ExpiryDate,
                DocumentType = type.Name,
                LocationName = db.OperationalLocationProjections.AsNoTracking()
                    .Where(location => location.BusinessId == businessId
                        && location.Id == employee.CurrentPrimaryLocationProjectionId)
                    .Select(location => location.Name).FirstOrDefault()
            };

        if (request.Status == "Expired")
            documents = documents.Where(row => row.ExpiryDate < asOfDate);
        else if (request.Status == "Expiring")
            documents = documents.Where(row => row.ExpiryDate >= asOfDate);
        else if (!string.IsNullOrWhiteSpace(request.Status))
            documents = documents.Where(_ => false);

        var totalCount = await documents.LongCountAsync(cancellationToken);
        var ordered = request.SortBy switch
        {
            "employeeCode" => request.Descending
                ? documents.OrderByDescending(row => row.EmployeeCode).ThenBy(row => row.EmployeeId)
                : documents.OrderBy(row => row.EmployeeCode).ThenBy(row => row.EmployeeId),
            "employeeName" => request.Descending
                ? documents.OrderByDescending(row => row.DisplayName).ThenBy(row => row.EmployeeId)
                : documents.OrderBy(row => row.DisplayName).ThenBy(row => row.EmployeeId),
            "category" => request.Descending
                ? documents.OrderByDescending(row => row.DocumentType).ThenBy(row => row.EmployeeId)
                : documents.OrderBy(row => row.DocumentType).ThenBy(row => row.EmployeeId),
            "status" => request.Descending
                ? documents.OrderByDescending(row => row.ExpiryDate < asOfDate).ThenBy(row => row.EmployeeId)
                : documents.OrderBy(row => row.ExpiryDate < asOfDate).ThenBy(row => row.EmployeeId),
            "locationName" => request.Descending
                ? documents.OrderByDescending(row => row.LocationName).ThenBy(row => row.EmployeeId)
                : documents.OrderBy(row => row.LocationName).ThenBy(row => row.EmployeeId),
            _ => request.Descending
                ? documents.OrderByDescending(row => row.ExpiryDate).ThenBy(row => row.EmployeeCode)
                    .ThenBy(row => row.EmployeeId)
                : documents.OrderBy(row => row.ExpiryDate).ThenBy(row => row.EmployeeCode)
                    .ThenBy(row => row.EmployeeId)
        };
        var page = await ordered.Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize).ToListAsync(cancellationToken);
        var items = page.Select(row =>
        {
            var expiryDate = row.ExpiryDate.GetValueOrDefault();
            return new HrCoreReportRow(
                row.EmployeeId, row.EmployeeCode, row.DisplayName, "DocumentExpiry",
                expiryDate, expiryDate < asOfDate ? "Expired" : "Expiring", row.LocationName,
                row.DocumentType, expiryDate.DayNumber - asOfDate.DayNumber);
        }).ToArray();
        return new HrCoreReportPage(request.Kind, items, request.Page, request.PageSize, totalCount);
    }

    private IEnumerable<HrCoreReportRow> ApplySafeSort(
        IReadOnlyCollection<HrCoreReportRow> rows, HrCoreReportQuery request)
    {
        var descending = request.Descending;
        return request.SortBy switch
        {
            "employeeCode" => descending
                ? rows.OrderByDescending(row => row.EmployeeCode).ThenBy(row => row.EmployeeId)
                : rows.OrderBy(row => row.EmployeeCode).ThenBy(row => row.EmployeeId),
            "employeeName" => descending
                ? rows.OrderByDescending(row => row.EmployeeName).ThenBy(row => row.EmployeeId)
                : rows.OrderBy(row => row.EmployeeName).ThenBy(row => row.EmployeeId),
            "category" => descending
                ? rows.OrderByDescending(row => row.Category).ThenBy(row => row.EffectiveDate)
                    .ThenBy(row => row.EmployeeId)
                : rows.OrderBy(row => row.Category).ThenBy(row => row.EffectiveDate)
                    .ThenBy(row => row.EmployeeId),
            "status" => descending
                ? rows.OrderByDescending(row => row.Status).ThenBy(row => row.EmployeeCode)
                    .ThenBy(row => row.EmployeeId)
                : rows.OrderBy(row => row.Status).ThenBy(row => row.EmployeeCode)
                    .ThenBy(row => row.EmployeeId),
            "locationName" => descending
                ? rows.OrderByDescending(row => row.LocationName).ThenBy(row => row.EmployeeCode)
                    .ThenBy(row => row.EmployeeId)
                : rows.OrderBy(row => row.LocationName).ThenBy(row => row.EmployeeCode)
                    .ThenBy(row => row.EmployeeId),
            _ => descending
                ? rows.OrderByDescending(row => row.EffectiveDate).ThenBy(row => row.EmployeeCode)
                    .ThenBy(row => row.EmployeeId)
                : rows.OrderBy(row => row.EffectiveDate).ThenBy(row => row.EmployeeCode)
                    .ThenBy(row => row.EmployeeId)
        };
    }

    private IQueryable<HrEmployee> ApplyEmployeeFilters(
        IQueryable<HrEmployee> query, HrCoreReportQuery request)
    {
        if (request.EmploymentStatus is not null)
            query = query.Where(employee => employee.CurrentEmploymentStatus == request.EmploymentStatus);
        if (request.EmploymentTypeId is long employmentTypeId)
            query = query.Where(employee => employee.CurrentEmploymentTypeId == employmentTypeId);
        if (request.PositionId is long positionId)
            query = query.Where(employee => employee.CurrentPositionId == positionId);
        if (request.DepartmentId is long departmentId)
            query = query.Where(employee => employee.CurrentOrgUnitId == departmentId);
        return query;
    }

}
