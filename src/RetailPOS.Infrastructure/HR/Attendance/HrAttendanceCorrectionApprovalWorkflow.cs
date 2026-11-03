using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Attendance;

public sealed class HrAttendanceCorrectionApprovalWorkflow(RetailPOSDbContext db, TimeProvider clock)
    : IApprovalKernelWorkflow, IApprovalTargetSnapshotReader
{
    public async Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId, long requesterUserId, SubmitApprovalCommand request, CancellationToken ct)
    {
        if (request.ProcessType != ApprovalProcessType.AttendanceCorrection)
            return null;
        if (request.TargetType != HrAttendanceCorrectionApprovalConstants.TargetType
            || !Guid.TryParse(request.TargetId, out var correctionId)
            || string.IsNullOrWhiteSpace(request.TargetPayload)
            || request.ScopeType != ApprovalScopeType.Location
            || !long.TryParse(request.ScopeReference, out var scopeLocationId)
            || scopeLocationId <= 0)
            throw new ArgumentException("Attendance correction approval requires its explicit location-scoped target.");

        var payload = JsonSerializer.Deserialize<HrAttendanceCorrectionTargetPayload>(request.TargetPayload)
            ?? throw new ArgumentException("Attendance correction target payload is invalid.");
        HrAttendanceCorrectionRules.ValidateShape(payload.CorrectionId, payload.DailyAttendanceId,
            payload.ExpectedProcessingVersion, payload.Values, payload.Reason);
        if (payload.CorrectionId != correctionId || payload.ExpectedProcessingVersion != request.TargetVersion
            || requesterUserId <= 0)
            throw new ArgumentException("Attendance correction target identity or version is invalid.");

        var canonicalPayload = JsonSerializer.Serialize(payload with { Reason = payload.Reason.Trim() });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
        if (!string.Equals(requestHash, request.RequestHash, StringComparison.OrdinalIgnoreCase))
            throw new ApprovalKernelConflictException("Attendance correction content does not match its request hash.");

        await HrAttendanceBusinessLock.LockAsync(db, businessId, ct);
        var prior = await db.Set<HrAttendanceCorrectionRequest>().SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == correctionId, ct);
        if (prior is not null)
            throw new ApprovalKernelConflictException(
                "Attendance correction ID already exists; retry the original correction request to retrieve its result.");

        var daily = await db.Set<HrDailyAttendance>().SingleOrDefaultAsync(
            row => row.BusinessId == businessId && row.Id == payload.DailyAttendanceId, ct)
            ?? throw new UnauthorizedAccessException("Attendance record was not found in the current Business.");
        if (daily.OperationalLocationProjectionId != scopeLocationId)
            throw new UnauthorizedAccessException("Attendance correction scope does not match the attendance location.");
        if (daily.ProcessingVersion != payload.ExpectedProcessingVersion)
            throw new HrAttendanceCorrectionConcurrencyException(
                "Attendance changed after the correction was prepared; reload before submitting.");
        if (daily.ProcessingVersion == int.MaxValue)
            throw new InvalidOperationException("Attendance processing version is exhausted.");
        if (await db.Set<HrAttendanceCorrectionRequest>().AnyAsync(row =>
                row.BusinessId == businessId && row.DailyAttendanceId == daily.Id
                && row.Status == "PendingApproval", ct))
            throw new ApprovalKernelConflictException("Attendance already has a pending correction.");

        var shift = JsonSerializer.Deserialize<HrShiftData>(daily.ShiftSnapshotJson)
            ?? throw new InvalidOperationException("Attendance has an invalid frozen shift snapshot.");
        HrAttendanceCorrectionRules.ValidateAgainstShift(daily.WorkDate, shift, payload.Values);
        var lastSequence = await db.Set<HrAttendanceCorrectionRequest>().Where(row =>
                row.BusinessId == businessId && row.DailyAttendanceId == daily.Id)
            .Select(row => (int?)row.CorrectionSequence).MaxAsync(ct) ?? 0;
        if (lastSequence == int.MaxValue)
            throw new InvalidOperationException("Attendance correction sequence is exhausted.");

        var before = Evidence(daily);
        var proposed = before with
        {
            Values = payload.Values,
            ProcessingVersion = daily.ProcessingVersion + 1,
            IsManuallyAdjusted = true
        };
        db.Add(new HrAttendanceCorrectionRequest
        {
            Id = correctionId,
            BusinessId = businessId,
            DailyAttendanceId = daily.Id,
            EmployeeId = daily.EmployeeId,
            WorkDate = daily.WorkDate,
            OperationalLocationProjectionId = daily.OperationalLocationProjectionId,
            CorrectionSequence = lastSequence + 1,
            ExpectedProcessingVersion = daily.ProcessingVersion,
            RequestHash = requestHash,
            Reason = payload.Reason.Trim(),
            BeforeEvidenceJson = JsonSerializer.Serialize(before),
            ProposedEvidenceJson = JsonSerializer.Serialize(proposed),
            Status = "PendingApproval",
            RequestedByUserId = requesterUserId,
            RequestedAtUtc = clock.GetUtcNow().UtcDateTime,
            Version = 1
        });
        return new ApprovalTargetSnapshot(daily.ProcessingVersion, requestHash);
    }

    public Task SubmissionCreatedAsync(
        long businessId, ApprovalInstance instance, CancellationToken ct)
    {
        if (instance.ProcessType != ApprovalProcessType.AttendanceCorrection)
            return Task.CompletedTask;
        if (instance.TargetType != HrAttendanceCorrectionApprovalConstants.TargetType
            || !Guid.TryParse(instance.TargetId, out var correctionId))
            throw new ApprovalKernelConflictException("Approval instance has an invalid attendance correction target.");
        var request = db.Set<HrAttendanceCorrectionRequest>().Local.SingleOrDefault(row =>
            row.BusinessId == businessId && row.Id == correctionId)
            ?? throw new ApprovalKernelConflictException("Prepared attendance correction request was not tracked.");
        request.ApprovalInstance = instance;
        return Task.CompletedTask;
    }

    public async Task DecisionCompletedAsync(
        long businessId, ApprovalInstance instance, long actorUserId, string? reason, CancellationToken ct)
    {
        if (instance.ProcessType != ApprovalProcessType.AttendanceCorrection)
            return;
        if (instance.TargetType != HrAttendanceCorrectionApprovalConstants.TargetType
            || !Guid.TryParse(instance.TargetId, out var correctionId))
            throw new ApprovalKernelConflictException("Approval instance has an invalid attendance correction target.");

        await HrAttendanceBusinessLock.LockAsync(db, businessId, ct);
        var request = await db.Set<HrAttendanceCorrectionRequest>().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == correctionId, ct)
            ?? throw new ApprovalKernelConflictException("Attendance correction target was not found.");
        if (request.Status != "PendingApproval" || request.ApprovalInstanceId != instance.Id)
            throw new ApprovalKernelConflictException("Attendance correction request is already terminal or mismatched.");

        request.DecidedByUserId = actorUserId;
        request.DecidedAtUtc = clock.GetUtcNow().UtcDateTime;
        request.DecisionReason = reason?.Trim();
        request.Version++;
        if (instance.Status == ApprovalInstanceStatus.Rejected)
        {
            request.Status = "Rejected";
            return;
        }
        if (instance.Status == ApprovalInstanceStatus.Cancelled)
        {
            request.Status = "Cancelled";
            return;
        }
        if (instance.Status != ApprovalInstanceStatus.Approved)
            throw new ApprovalKernelConflictException("Only a terminal approval decision can complete a correction.");

        var daily = await db.Set<HrDailyAttendance>().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == request.DailyAttendanceId, ct)
            ?? throw new ApprovalKernelConflictException("Attendance record for the correction was not found.");
        if (daily.ProcessingVersion != request.ExpectedProcessingVersion)
            throw new HrAttendanceCorrectionConcurrencyException(
                "Attendance changed while its correction was awaiting approval.");
        if (daily.ProcessingVersion == int.MaxValue)
            throw new InvalidOperationException("Attendance processing version is exhausted.");

        var proposed = JsonSerializer.Deserialize<HrAttendanceCorrectionEvidence>(request.ProposedEvidenceJson)
            ?? throw new InvalidDataException("Attendance correction proposal evidence is invalid.");
        Apply(daily, proposed.Values);
        daily.IsManuallyAdjusted = true;
        daily.ProcessingVersion++;
        daily.LastProcessedAtUtc = clock.GetUtcNow().UtcDateTime;
        request.Status = "Approved";
        request.AppliedAfterEvidenceJson = JsonSerializer.Serialize(Evidence(daily));
    }

    public async Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken ct)
    {
        if (targetType != HrAttendanceCorrectionApprovalConstants.TargetType
            || !Guid.TryParse(targetId, out var correctionId))
            return null;
        var request = await db.Set<HrAttendanceCorrectionRequest>().AsNoTracking().SingleOrDefaultAsync(row =>
            row.BusinessId == businessId && row.Id == correctionId
            && row.Status == "PendingApproval", ct);
        if (request is null) return null;
        var currentVersion = await db.Set<HrDailyAttendance>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == request.DailyAttendanceId)
            .Select(row => (int?)row.ProcessingVersion).SingleOrDefaultAsync(ct);
        return currentVersion.HasValue
            ? new ApprovalTargetSnapshot(currentVersion.Value, request.RequestHash)
            : null;
    }

    private static HrAttendanceCorrectionEvidence Evidence(HrDailyAttendance daily) => new(
        daily.Id, daily.BusinessId, daily.EmployeeId, daily.WorkDate, daily.OperationalLocationProjectionId,
        daily.ShiftId, daily.RosterAssignmentId, daily.DayClassification, daily.TimeZoneIdSnapshot,
        Values(daily), daily.OvertimeMinutesCalculated, daily.ProcessingVersion, daily.IsManuallyAdjusted,
        JsonSerializer.Deserialize<long[]>(daily.PunchIdsJson) ?? [], daily.ShiftSnapshotJson,
        daily.LocationSnapshotJson, daily.CalendarEvidenceJson);

    private static HrAttendanceCorrectionValues Values(HrDailyAttendance daily) => new(
        daily.FirstInAtLocal, daily.LastOutAtLocal, daily.WorkedMinutes, daily.BreakMinutes,
        daily.LateMinutes, daily.EarlyLeaveMinutes, daily.Status);

    private static void Apply(HrDailyAttendance daily, HrAttendanceCorrectionValues values)
    {
        daily.FirstInAtLocal = values.FirstInAtLocal;
        daily.LastOutAtLocal = values.LastOutAtLocal;
        daily.WorkedMinutes = values.WorkedMinutes;
        daily.BreakMinutes = values.BreakMinutes;
        daily.LateMinutes = values.LateMinutes;
        daily.EarlyLeaveMinutes = values.EarlyLeaveMinutes;
        daily.Status = values.Status;
    }
}

public sealed class HrAttendanceCorrectionConcurrencyException(string message) : InvalidOperationException(message);

internal static class HrAttendanceBusinessLock
{
    public static async Task LockAsync(RetailPOSDbContext db, long businessId, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({businessId ^ (long.MinValue + 2)})", ct);
    }
}
