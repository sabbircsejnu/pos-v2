using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.HR.Overtime;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Overtime;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Attendance;

namespace RetailPOS.Infrastructure.HR.Overtime;

public sealed class HrOvertimeStore(RetailPOSDbContext db, TimeProvider clock)
    : IHrOvertimeStore, IApprovalKernelWorkflow
{
    public async Task<HrOvertimePolicyModel> CreatePolicyAsync(
        long businessId, HrOvertimePolicyInput input, CancellationToken cancellationToken)
    {
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var policy = new HrOvertimePolicyVersion
        {
            BusinessId = businessId,
            Code = input.Code.Trim(),
            Name = input.Name.Trim(),
            ScopeType = input.ScopeType,
            ScopeReference = input.ScopeType == HrOvertimePolicyScopeType.Business
                ? null : input.ScopeReference?.Trim(),
            VersionNo = input.VersionNo,
            EffectiveFrom = input.EffectiveFrom,
            EffectiveTo = input.EffectiveTo,
            MinimumEligibleMinutes = input.MinimumEligibleMinutes,
            MaximumEligibleMinutes = input.MaximumEligibleMinutes,
            RoundingUnitMinutes = input.RoundingUnitMinutes,
            RoundingMethod = input.RoundingMethod,
            IncludePreShift = input.IncludePreShift,
            IncludePostShift = input.IncludePostShift,
            AllowNormalWorkingDay = input.AllowNormalWorkingDay,
            AllowWeeklyOff = input.AllowWeeklyOff,
            AllowHoliday = input.AllowHoliday,
            AllowSpecialDay = input.AllowSpecialDay,
            RateType = input.RateType,
            NormalDayRate = input.NormalDayRate,
            WeeklyOffRate = input.WeeklyOffRate,
            HolidayRate = input.HolidayRate,
            SpecialDayRate = input.SpecialDayRate,
            IsActive = input.IsActive
        };
        HrOvertimeRules.ValidatePolicy(policy);

        var latestVersion = await db.Set<HrOvertimePolicyVersion>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Code == policy.Code)
            .Select(row => (int?)row.VersionNo).MaxAsync(cancellationToken) ?? 0;
        if (policy.VersionNo != latestVersion + 1)
            throw new InvalidOperationException("Overtime policy versions must be created sequentially.");

        var sameScopeVersions = await db.Set<HrOvertimePolicyVersion>()
            .Where(row => row.BusinessId == businessId && row.Code == policy.Code
                && row.ScopeType == policy.ScopeType && row.ScopeReference == policy.ScopeReference)
            .OrderByDescending(row => row.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var overlapsFuture = sameScopeVersions.Any(row =>
            row.EffectiveFrom >= policy.EffectiveFrom
            && (policy.EffectiveTo == null || row.EffectiveFrom <= policy.EffectiveTo)
            && (row.EffectiveTo == null || row.EffectiveTo >= policy.EffectiveFrom));
        if (overlapsFuture)
            throw new InvalidOperationException("Overtime policy effective dates cannot overlap within a scope.");
        var predecessor = sameScopeVersions.FirstOrDefault(row =>
            row.IsActive && row.EffectiveFrom < policy.EffectiveFrom
            && (row.EffectiveTo is null || row.EffectiveTo >= policy.EffectiveFrom));
        if (predecessor is not null)
        {
            if (predecessor.Version == long.MaxValue)
                throw new InvalidOperationException("Overtime policy concurrency version is exhausted.");
            predecessor.EffectiveTo = policy.EffectiveFrom.AddDays(-1);
            predecessor.Version++;
        }

        db.Add(policy);
        await SaveChangesWithConflictMappingAsync(cancellationToken);
        return Map(policy);
    }

    public async Task<IReadOnlyList<HrOvertimePolicyModel>> ListPoliciesAsync(
        long businessId, CancellationToken cancellationToken) =>
        (await db.Set<HrOvertimePolicyVersion>().AsNoTracking()
            .Where(row => row.BusinessId == businessId)
            .OrderBy(row => row.Code).ThenBy(row => row.ScopeType)
            .ThenBy(row => row.ScopeReference).ThenBy(row => row.VersionNo)
            .ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<IReadOnlyList<HrOvertimeModel>> CalculateAsync(
        long businessId,
        long actorUserId,
        DateOnly from,
        DateOnly to,
        long? employeeId,
        long? locationId,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        if (actorUserId <= 0 || scope.BusinessId != businessId)
            throw new UnauthorizedAccessException("Overtime calculation scope or actor is invalid.");
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);

        var attendanceQuery = db.Set<HrDailyAttendance>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.WorkDate >= from && row.WorkDate <= to
                && (employeeId == null || row.EmployeeId == employeeId)
                && (locationId == null || row.OperationalLocationProjectionId == locationId));
        var locationIds = await AccessibleLocationIdsAsync(businessId, scope, cancellationToken);
        attendanceQuery = attendanceQuery.Where(row =>
            Enumerable.Contains(locationIds, row.OperationalLocationProjectionId));
        var attendanceRows = await attendanceQuery.OrderBy(row => row.WorkDate).ThenBy(row => row.EmployeeId)
            .ThenBy(row => row.Id).ToListAsync(cancellationToken);
        var changedIds = new List<long>();
        var changedEntries = new List<HrOvertimeEntry>(attendanceRows.Count);

        foreach (var attendance in attendanceRows)
        {
            if (attendance.FirstInAtLocal is null || attendance.LastOutAtLocal is null
                || attendance.WorkedMinutes <= 0)
            {
                await SupersedeIfPresentAsync(businessId, attendance.Id, cancellationToken);
                continue;
            }

            var shift = JsonSerializer.Deserialize<HrShiftData>(attendance.ShiftSnapshotJson)
                ?? throw new InvalidOperationException(
                    $"Daily attendance {attendance.Id} has invalid frozen shift evidence.");
            if (!shift.OvertimeEligible)
            {
                await SupersedeIfPresentAsync(businessId, attendance.Id, cancellationToken);
                continue;
            }

            var roster = await db.Set<HrRosterAssignment>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.BusinessId == businessId
                    && row.Id == attendance.RosterAssignmentId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Daily attendance {attendance.Id} references a missing published roster.");
            if (roster.Status != "Published" || roster.EmployeeId != attendance.EmployeeId
                || roster.WorkDate != attendance.WorkDate || roster.ShiftId != attendance.ShiftId)
                throw new InvalidOperationException(
                    $"Daily attendance {attendance.Id} does not match its frozen published roster.");

            var dayCategory = ResolveDayCategory(attendance.DayClassification, roster.WorkKind);
            var policy = await ResolvePolicyAsync(businessId, attendance, dayCategory, cancellationToken);
            if (policy is null)
            {
                await SupersedeIfPresentAsync(businessId, attendance.Id, cancellationToken);
                continue;
            }

            var scheduledMinutes = HrShiftRules.DurationMinutes(
                    shift.StartTimeLocal, shift.EndTimeLocal, shift.CrossesMidnight)
                - shift.Breaks.Where(item => !item.IsPaid).Sum(item => item.DurationMinutes);
            var scheduledStart = attendance.WorkDate.ToDateTime(shift.StartTimeLocal, DateTimeKind.Unspecified);
            var scheduledEnd = scheduledStart.AddMinutes(
                HrShiftRules.DurationMinutes(shift.StartTimeLocal, shift.EndTimeLocal, shift.CrossesMidnight));
            var preShiftMinutes = Math.Max(0,
                (int)Math.Floor((scheduledStart - attendance.FirstInAtLocal.Value).TotalMinutes));
            var postShiftMinutes = Math.Max(0,
                (int)Math.Floor((attendance.LastOutAtLocal.Value - scheduledEnd).TotalMinutes));
            var calculationInput = new HrOvertimeCalculationInput(
                attendance.WorkedMinutes,
                attendance.BreakMinutes,
                scheduledMinutes,
                preShiftMinutes,
                postShiftMinutes,
                dayCategory);
            var calculation = HrOvertimeRules.Calculate(calculationInput, policy);
            var attendanceSnapshot = JsonSerializer.Serialize(new
            {
                attendance.Id,
                attendance.BusinessId,
                attendance.EmployeeId,
                attendance.WorkDate,
                attendance.OperationalLocationProjectionId,
                attendance.SourceLocationTypeSnapshot,
                attendance.ShiftId,
                attendance.RosterAssignmentId,
                attendance.DayClassification,
                attendance.FirstInAtLocal,
                attendance.LastOutAtLocal,
                attendance.WorkedMinutes,
                attendance.BreakMinutes,
                attendance.ProcessingVersion,
                attendance.IsManuallyAdjusted,
                attendance.ShiftSnapshotJson,
                attendance.LocationSnapshotJson,
                attendance.CalendarEvidenceJson,
                roster.WorkKind
            });
            var policySnapshot = JsonSerializer.Serialize(policy);
            var requestHash = HrOvertimeRules.CreateRequestHash(attendance.Id, attendance.EmployeeId,
                attendance.WorkDate, attendance.ProcessingVersion, policy.Id, policy.VersionNo,
                calculation.CalculatedMinutes, calculation.DayCategory, calculation.Fingerprint);

            var existing = await db.Set<HrOvertimeEntry>().Include(row => row.CalculationEvidence)
                .SingleOrDefaultAsync(row => row.BusinessId == businessId
                    && row.DailyAttendanceId == attendance.Id, cancellationToken);
            if (existing is null)
            {
                existing = new HrOvertimeEntry
                {
                    BusinessId = businessId,
                    EmployeeId = attendance.EmployeeId,
                    WorkDate = attendance.WorkDate,
                    DailyAttendanceId = attendance.Id,
                    OperationalLocationProjectionId = attendance.OperationalLocationProjectionId,
                    DayCategory = calculation.DayCategory,
                    AttendanceProcessingVersion = attendance.ProcessingVersion,
                    PolicyVersionId = policy.Id,
                    PolicyVersionNo = policy.VersionNo,
                    EligibleMinutes = calculation.EligibleMinutes,
                    CalculatedMinutes = calculation.CalculatedMinutes,
                    Status = "Calculated",
                    CalculationVersion = 1,
                    CalculationFingerprint = calculation.Fingerprint,
                    RequestHash = requestHash,
                    PolicySnapshotJson = policySnapshot,
                    AttendanceSnapshotJson = attendanceSnapshot,
                    CalculationInputsJson = calculation.InputsJson,
                    Version = 1
                };
                db.Add(existing);
                existing.CalculationEvidence.Add(CreateEvidence(
                    existing, actorUserId, calculation, attendance.ProcessingVersion, policy));
                changedEntries.Add(existing);
            }
            else if (existing.Status is "Calculated" or "Cancelled" or "Rejected" or "Superseded"
                && (existing.Status == "Superseded"
                    || existing.AttendanceProcessingVersion != attendance.ProcessingVersion
                    || !string.Equals(existing.CalculationFingerprint, calculation.Fingerprint, StringComparison.Ordinal)))
            {
                if (existing.CalculationVersion == int.MaxValue || existing.Version == long.MaxValue)
                    throw new InvalidOperationException("Overtime calculation version is exhausted.");
                existing.CalculationVersion++;
                existing.AttendanceProcessingVersion = attendance.ProcessingVersion;
                existing.PolicyVersionId = policy.Id;
                existing.PolicyVersionNo = policy.VersionNo;
                existing.EligibleMinutes = calculation.EligibleMinutes;
                existing.CalculatedMinutes = calculation.CalculatedMinutes;
                existing.DayCategory = calculation.DayCategory;
                existing.CalculationFingerprint = calculation.Fingerprint;
                existing.RequestHash = requestHash;
                existing.PolicySnapshotJson = policySnapshot;
                existing.AttendanceSnapshotJson = attendanceSnapshot;
                existing.CalculationInputsJson = calculation.InputsJson;
                existing.Status = "Calculated";
                existing.ApprovedMinutes = null;
                existing.ApprovalInstanceId = null;
                existing.Version++;
                existing.CalculationEvidence.Add(CreateEvidence(
                    existing, actorUserId, calculation, attendance.ProcessingVersion, policy));
                changedEntries.Add(existing);
            }
            else if (existing.Status == "PendingApproval"
                && (existing.AttendanceProcessingVersion != attendance.ProcessingVersion
                    || existing.PolicyVersionId != policy.Id
                    || existing.CalculationFingerprint != calculation.Fingerprint))
                throw new InvalidOperationException(
                    "Pending overtime is stale; cancel its approval before recalculating.");

            changedEntries.Add(existing);
        }

        await SaveChangesWithConflictMappingAsync(cancellationToken);
        changedIds.AddRange(changedEntries.Select(entry => entry.Id));
        var results = new List<HrOvertimeModel>(changedIds.Distinct().Count());
        foreach (var id in changedIds.Distinct())
            results.Add(await GetAsync(businessId, id, cancellationToken)
                ?? throw new InvalidOperationException("Calculated overtime could not be reloaded."));
        return results;
    }

    private async Task SupersedeIfPresentAsync(
        long businessId, long attendanceId, CancellationToken cancellationToken)
    {
        var existing = await db.Set<HrOvertimeEntry>()
            .SingleOrDefaultAsync(row => row.BusinessId == businessId
                && row.DailyAttendanceId == attendanceId, cancellationToken);
        if (existing is null || existing.Status == "PayrollEligible")
            return;
        if (existing.Status == "PendingApproval")
            throw new ApprovalKernelConflictException(
                "Pending overtime is stale; cancel its approval before recalculating.");
        if (existing.Status is not ("Calculated" or "Cancelled" or "Rejected" or "Superseded"))
            throw new InvalidOperationException("Overtime entry has an unsupported lifecycle state.");
        if (existing.Status == "Superseded")
            return;
        if (existing.Version == long.MaxValue)
            throw new InvalidOperationException("Overtime entry concurrency version is exhausted.");
        existing.Status = "Superseded";
        existing.Version++;
    }

    private async Task SaveChangesWithConflictMappingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new HrOvertimeConcurrencyException(exception);
        }
    }

    public async Task<IReadOnlyList<HrOvertimeModel>> ListAsync(
        long businessId,
        DateOnly from,
        DateOnly to,
        long? employeeId,
        long? locationId,
        string? status,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.BusinessId != businessId)
            throw new UnauthorizedAccessException("Overtime report scope is outside Business.");
        var query = db.Set<HrOvertimeEntry>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.WorkDate >= from && row.WorkDate <= to
                && (employeeId == null || row.EmployeeId == employeeId)
                && (locationId == null || row.OperationalLocationProjectionId == locationId));
        var locationIds = await AccessibleLocationIdsAsync(businessId, scope, cancellationToken);
        query = query.Where(row => Enumerable.Contains(locationIds, row.OperationalLocationProjectionId));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(row => row.Status == status);
        var ids = await query.OrderByDescending(row => row.WorkDate).ThenBy(row => row.EmployeeId)
            .Select(row => row.Id).Take(500).ToArrayAsync(cancellationToken);
        var models = new List<HrOvertimeModel>(ids.Length);
        foreach (var id in ids)
            if (await GetAsync(businessId, id, cancellationToken) is { } model)
                models.Add(model);
        return models;
    }

    public async Task<HrOvertimeModel?> GetAsync(
        long businessId, long overtimeId, CancellationToken cancellationToken)
    {
        var entry = await db.Set<HrOvertimeEntry>().AsNoTracking()
            .Include(row => row.ApprovalInstance)
            .Include(row => row.CalculationEvidence)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == overtimeId,
                cancellationToken);
        if (entry is null)
            return null;
        var employee = await db.Set<HrEmployee>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == entry.EmployeeId)
            .Select(row => new { row.EmployeeCode, row.DisplayName })
            .SingleAsync(cancellationToken);
        return new HrOvertimeModel(
            entry.Id, entry.EmployeeId, employee.EmployeeCode, employee.DisplayName,
            entry.WorkDate, entry.DailyAttendanceId, entry.OperationalLocationProjectionId,
            entry.DayCategory, entry.AttendanceProcessingVersion, entry.PolicyVersionId,
            entry.PolicyVersionNo, entry.EligibleMinutes, entry.CalculatedMinutes,
            entry.ApprovedMinutes, entry.Status, entry.CalculationVersion, entry.ApprovalInstanceId,
            entry.ApprovalInstance?.Status, entry.ApprovalInstance?.CurrentStepSequence, entry.Version,
            entry.CalculationEvidence.OrderBy(item => item.CalculationVersion)
                .Select(item => new HrOvertimeCalculationEvidenceModel(
                    item.CalculationVersion, item.AttendanceProcessingVersion, item.PolicyVersionId,
                    item.EligibleMinutes, item.CalculatedMinutes, item.DayCategory,
                    item.CalculationFingerprint, item.PolicySnapshotJson,
                    item.AttendanceSnapshotJson, item.CalculationInputsJson,
                    new DateTimeOffset(DateTime.SpecifyKind(item.CreatedAtUtc, DateTimeKind.Utc)),
                    item.CreatedByUserId)).ToArray());
    }

    public async Task<HrOvertimeScope?> GetScopeAsync(
        long businessId, long overtimeId, CancellationToken cancellationToken) =>
        await db.Set<HrOvertimeEntry>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == overtimeId)
            .Select(row => new HrOvertimeScope(row.EmployeeId, row.OperationalLocationProjectionId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<long?> GetApprovalInstanceIdAsync(
        long businessId, long overtimeId, CancellationToken cancellationToken) =>
        db.Set<HrOvertimeEntry>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == overtimeId
                && row.Status == "PendingApproval")
            .Select(row => row.ApprovalInstanceId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken)
    {
        if (targetType != HrOvertimeApprovalConstants.TargetType
            || !long.TryParse(targetId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return null;
        var row = await db.Set<HrOvertimeEntry>().AsNoTracking()
            .Include(item => item.ApprovalInstance)
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == id,
                cancellationToken);
        if (row is null)
            return null;
        var targetVersion = row.Status == "PendingApproval" && row.ApprovalInstance is not null
            ? row.ApprovalInstance.TargetVersion
            : row.Version;
        return new ApprovalTargetSnapshot(targetVersion, row.RequestHash);
    }

    public async Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId,
        long requesterUserId,
        SubmitApprovalCommand approval,
        CancellationToken cancellationToken)
    {
        if (approval.ProcessType != ApprovalProcessType.Overtime)
            return null;
        var id = ParseTarget(approval.TargetType, approval.TargetId);
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var row = await db.Set<HrOvertimeEntry>().Include(item => item.CalculationEvidence)
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == id,
                cancellationToken)
            ?? throw new KeyNotFoundException("Overtime entry was not found in this Business.");
        if (row.Status != "Calculated" || row.Version != approval.TargetVersion
            || !string.Equals(row.RequestHash, approval.RequestHash, StringComparison.OrdinalIgnoreCase)
            || row.CalculationEvidence.Count == 0)
            throw new ApprovalKernelConflictException(
                "Overtime entry changed, is not calculated, or has no frozen calculation evidence.");

        var attendance = await db.Set<HrDailyAttendance>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.BusinessId == businessId
                && item.Id == row.DailyAttendanceId, cancellationToken)
            ?? throw new ApprovalKernelConflictException("Overtime attendance evidence is missing.");
        if (attendance.ProcessingVersion != row.AttendanceProcessingVersion)
            throw new ApprovalKernelConflictException(
                "Attendance changed after overtime calculation; recalculate before submission.");

        row.Status = "PendingApproval";
        row.Version++;
        return new ApprovalTargetSnapshot(approval.TargetVersion, row.RequestHash);
    }

    public Task SubmissionCreatedAsync(
        long businessId, ApprovalInstance instance, CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.Overtime)
            return Task.CompletedTask;
        var id = ParseTarget(instance.TargetType, instance.TargetId);
        var row = db.Set<HrOvertimeEntry>().Local.SingleOrDefault(item =>
            item.BusinessId == businessId && item.Id == id)
            ?? throw new ApprovalKernelConflictException("Prepared overtime entry was not tracked.");
        if (row.Status != "PendingApproval" || row.Version != instance.TargetVersion + 1)
            throw new ApprovalKernelConflictException("Overtime entry no longer matches its approval instance.");
        row.ApprovalInstance = instance;
        return Task.CompletedTask;
    }

    public async Task DecisionCompletedAsync(
        long businessId,
        ApprovalInstance instance,
        long actorUserId,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (instance.ProcessType != ApprovalProcessType.Overtime)
            return;
        var id = ParseTarget(instance.TargetType, instance.TargetId);
        await HrAttendanceBusinessLock.LockAsync(db, businessId, cancellationToken);
        var row = await db.Set<HrOvertimeEntry>()
            .SingleOrDefaultAsync(item => item.BusinessId == businessId && item.Id == id,
                cancellationToken)
            ?? throw new ApprovalKernelConflictException("Overtime entry target was not found.");
        if (row.Status != "PendingApproval" || row.ApprovalInstanceId != instance.Id)
            throw new ApprovalKernelConflictException("Overtime entry is terminal or mismatched.");
        var currentAttendanceVersion = await db.Set<HrDailyAttendance>().AsNoTracking()
            .Where(item => item.BusinessId == businessId && item.Id == row.DailyAttendanceId)
            .Select(item => (int?)item.ProcessingVersion)
            .SingleOrDefaultAsync(cancellationToken);
        if (instance.Status == ApprovalInstanceStatus.Approved)
        {
            if (currentAttendanceVersion != row.AttendanceProcessingVersion)
                throw new ApprovalKernelConflictException(
                    "Attendance changed before final approval; cancel and recalculate overtime.");
            row.Status = "PayrollEligible";
            row.ApprovedMinutes = row.CalculatedMinutes;
        }
        else if (instance.Status == ApprovalInstanceStatus.Rejected)
            row.Status = "Rejected";
        else if (instance.Status == ApprovalInstanceStatus.Cancelled)
            row.Status = "Cancelled";
        else
            throw new ApprovalKernelConflictException("A non-terminal approval cannot update overtime.");
        row.Version++;
    }

    private async Task<HrOvertimePolicyVersion?> ResolvePolicyAsync(
        long businessId,
        HrDailyAttendance attendance,
        string dayCategory,
        CancellationToken cancellationToken)
    {
        var location = attendance.OperationalLocationProjectionId.ToString(CultureInfo.InvariantCulture);
        var employee = attendance.EmployeeId.ToString(CultureInfo.InvariantCulture);
        var policies = await db.Set<HrOvertimePolicyVersion>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.IsActive
                && row.EffectiveFrom <= attendance.WorkDate
                && (row.EffectiveTo == null || row.EffectiveTo >= attendance.WorkDate))
            .ToListAsync(cancellationToken);
        var policy = policies.Where(policy => policy.ScopeType switch
            {
                HrOvertimePolicyScopeType.Employee => policy.ScopeReference == employee,
                HrOvertimePolicyScopeType.Location => policy.ScopeReference == location,
                HrOvertimePolicyScopeType.LocationType =>
                    policy.ScopeReference == attendance.SourceLocationTypeSnapshot,
                HrOvertimePolicyScopeType.Business => true,
                _ => false
            })
            .OrderBy(policy => policy.ScopeType switch
            {
                HrOvertimePolicyScopeType.Employee => 0,
                HrOvertimePolicyScopeType.Location => 1,
                HrOvertimePolicyScopeType.LocationType => 2,
                HrOvertimePolicyScopeType.Business => 3,
                _ => 4
            })
            .ThenByDescending(policy => policy.VersionNo)
            .FirstOrDefault();
        return policy is not null && IsEnabledForCategory(policy, dayCategory) ? policy : null;
    }

    private static bool IsEnabledForCategory(HrOvertimePolicyVersion policy, string category) =>
        category switch
        {
            "NormalWorkingDay" => policy.AllowNormalWorkingDay,
            "WeeklyOff" => policy.AllowWeeklyOff,
            "Holiday" => policy.AllowHoliday,
            "SpecialDay" => policy.AllowSpecialDay,
            _ => false
        };

    private static string ResolveDayCategory(string classification, string workKind) =>
        string.Equals(workKind, "Special", StringComparison.Ordinal)
            || classification == "SpecialDay" ? "SpecialDay"
            : classification switch
            {
                "Holiday" => "Holiday",
                "WeeklyOff" => "WeeklyOff",
                "NormalWorkingDay" => "NormalWorkingDay",
                _ => throw new InvalidOperationException("Attendance has an unsupported day classification.")
            };

    private async Task<long[]> AccessibleLocationIdsAsync(
        long businessId,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken)
    {
        var locations = await db.Set<OperationalLocationProjection>().AsNoTracking()
            .Where(row => row.BusinessId == businessId)
            .Select(row => new { row.Id, row.SourceEntityType, row.SourceEntityId })
            .ToListAsync(cancellationToken);
        if (scope.AllowedSources is null)
            return locations.Select(row => row.Id).ToArray();
        var allowed = scope.AllowedSources.ToHashSet();
        return locations.Where(row => allowed.Contains(new OperationalLocationSourceIdentity(
                row.SourceEntityType, row.SourceEntityId)))
            .Select(row => row.Id).ToArray();
    }

    private HrOvertimeCalculationEvidence CreateEvidence(
        HrOvertimeEntry entry,
        long actorUserId,
        HrOvertimeCalculationResult calculation,
        int attendanceVersion,
        HrOvertimePolicyVersion policy) =>
        new()
        {
            BusinessId = entry.BusinessId,
            OvertimeEntryId = entry.Id,
            CalculationVersion = entry.CalculationVersion,
            AttendanceProcessingVersion = attendanceVersion,
            PolicyVersionId = policy.Id,
            EligibleMinutes = calculation.EligibleMinutes,
            CalculatedMinutes = calculation.CalculatedMinutes,
            DayCategory = calculation.DayCategory,
            CalculationFingerprint = calculation.Fingerprint,
            PolicySnapshotJson = JsonSerializer.Serialize(policy),
            AttendanceSnapshotJson = entry.AttendanceSnapshotJson,
            CalculationInputsJson = calculation.InputsJson,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            CreatedByUserId = actorUserId
        };

    private static HrOvertimePolicyModel Map(HrOvertimePolicyVersion row) => new(
        row.Id, row.Code, row.Name, row.ScopeType, row.ScopeReference, row.VersionNo,
        row.EffectiveFrom, row.EffectiveTo, row.MinimumEligibleMinutes, row.MaximumEligibleMinutes,
        row.RoundingUnitMinutes, row.RoundingMethod, row.IncludePreShift, row.IncludePostShift,
        row.AllowNormalWorkingDay, row.AllowWeeklyOff, row.AllowHoliday, row.AllowSpecialDay,
        row.RateType, row.NormalDayRate, row.WeeklyOffRate, row.HolidayRate, row.SpecialDayRate, row.IsActive);

    private static long ParseTarget(string targetType, string targetId)
    {
        if (targetType != HrOvertimeApprovalConstants.TargetType
            || !long.TryParse(targetId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0)
            throw new ArgumentException("Overtime approval target is invalid.");
        return id;
    }

}
