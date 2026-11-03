using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Attendance;

public sealed class HrAttendanceCorrectionStore(RetailPOSDbContext db) : IHrAttendanceCorrectionStore
{
    public async Task<long> GetLocationIdAsync(long businessId, long dailyAttendanceId, CancellationToken ct) =>
        await db.Set<HrDailyAttendance>().AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.Id == dailyAttendanceId)
            .Select(row => (long?)row.OperationalLocationProjectionId).SingleOrDefaultAsync(ct)
        ?? throw new KeyNotFoundException("Daily attendance record was not found in this Business.");

    public async Task<HrAttendanceCorrectionExistingSubmission?> FindSubmissionAsync(
        long businessId, Guid correctionId, CancellationToken ct)
    {
        var request = await db.Set<HrAttendanceCorrectionRequest>()
            .AsNoTracking().Include(row => row.ApprovalInstance)
            .SingleOrDefaultAsync(row => row.BusinessId == businessId && row.Id == correctionId, ct);
        return request is null
            ? null
            : new(request.Id, request.RequestHash, request.ApprovalInstanceId, request.ApprovalInstance.Status,
                request.ApprovalInstance.CurrentStepSequence);
    }

    public async Task<IReadOnlyList<HrAttendanceCorrectionResponse>> ListAsync(
        long businessId, long dailyAttendanceId, CancellationToken ct)
    {
        var rows = await db.Set<HrAttendanceCorrectionRequest>()
            .AsNoTracking().Where(row => row.BusinessId == businessId && row.DailyAttendanceId == dailyAttendanceId)
            .OrderBy(row => row.CorrectionSequence).ToListAsync(ct);
        return rows.Select(row => new HrAttendanceCorrectionResponse(
            row.Id, row.DailyAttendanceId, row.EmployeeId,
            row.WorkDate,
            row.OperationalLocationProjectionId, row.CorrectionSequence, row.ExpectedProcessingVersion, row.Status,
            row.Reason, row.BeforeEvidenceJson, row.ProposedEvidenceJson, row.AppliedAfterEvidenceJson,
            row.RequestedByUserId, Utc(row.RequestedAtUtc), row.DecidedByUserId,
            row.DecidedAtUtc.HasValue ? Utc(row.DecidedAtUtc.Value) : null, row.DecisionReason,
            row.ApprovalInstanceId, row.Version)).ToArray();
    }

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
