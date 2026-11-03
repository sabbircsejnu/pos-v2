using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.Application.HR.Attendance;

public sealed record HrDailyAttendanceResponse(long Id, long BusinessId, long EmployeeId, DateOnly WorkDate,
    long OperationalLocationProjectionId, long ShiftId, long RosterAssignmentId, string DayClassification,
    string? TimeZoneIdSnapshot, DateTime? FirstInAtLocal, DateTime? LastOutAtLocal, int WorkedMinutes,
    int BreakMinutes, int LateMinutes, int EarlyLeaveMinutes, int OvertimeMinutesCalculated,
    string Status, int ProcessingVersion, DateTimeOffset LastProcessedAtUtc, bool IsManuallyAdjusted,
    IReadOnlyList<long> PunchIds);

public sealed record ProcessHrAttendanceCommand(DateOnly From, DateOnly To, long? EmployeeId = null)
    : IRequest<IReadOnlyList<HrDailyAttendanceResponse>>;
public sealed record GetHrDailyAttendanceQuery(DateOnly From, DateOnly To, long? EmployeeId = null)
    : IRequest<IReadOnlyList<HrDailyAttendanceResponse>>;

public interface IHrAttendanceProcessingStore
{
    Task<IReadOnlyList<HrDailyAttendanceResponse>> ProcessAsync(long businessId, DateOnly from, DateOnly to,
        long? employeeId, HrOperationalLocationReadScope scope, CancellationToken ct);
    Task<IReadOnlyList<HrDailyAttendanceResponse>> ListAsync(long businessId, DateOnly from, DateOnly to,
        long? employeeId, HrOperationalLocationReadScope scope, CancellationToken ct);
}

public sealed class HrAttendanceProcessingHandlers(IHrOrganisationAccess access,
    IHrAttendanceProcessingStore store, IRequiredAuditTransaction audit) :
    IRequestHandler<ProcessHrAttendanceCommand, IReadOnlyList<HrDailyAttendanceResponse>>,
    IRequestHandler<GetHrDailyAttendanceQuery, IReadOnlyList<HrDailyAttendanceResponse>>
{
    public async Task<IReadOnlyList<HrDailyAttendanceResponse>> Handle(ProcessHrAttendanceCommand request, CancellationToken ct)
    {
        Validate(request.From, request.To, request.EmployeeId);
        var business = access.RequireBusinessId();
        await access.EnsureAsync(business, HrOrganisationOperation.ProcessAttendance, null, ct);
        access.RequireRealUserId();
        var scope = await access.GetLocationReadScopeAsync(business, ct);
        return await audit.ExecuteAsync(new RequiredAuditOperation(business, "hr.attendance.processed", "HR",
            "Processed published attendance rosters.", "HrDailyAttendance", $"{request.From:yyyy-MM-dd}:{request.To:yyyy-MM-dd}"),
            token => store.ProcessAsync(business, request.From, request.To, request.EmployeeId, scope, token), ct);
    }

    public async Task<IReadOnlyList<HrDailyAttendanceResponse>> Handle(GetHrDailyAttendanceQuery request, CancellationToken ct)
    {
        Validate(request.From, request.To, request.EmployeeId);
        var business = access.RequireBusinessId();
        await access.EnsureAsync(business, HrOrganisationOperation.ViewAttendance, null, ct);
        return await store.ListAsync(business, request.From, request.To, request.EmployeeId,
            await access.GetLocationReadScopeAsync(business, ct), ct);
    }

    private static void Validate(DateOnly from, DateOnly to, long? employeeId)
    {
        if (from == default || to < from || to.DayNumber - from.DayNumber > 366 || employeeId is <= 0)
            throw new ArgumentException("Provide an inclusive attendance range of at most 367 days and a valid employee filter.");
    }
}
