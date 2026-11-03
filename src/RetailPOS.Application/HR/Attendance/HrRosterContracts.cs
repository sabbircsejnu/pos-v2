using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Attendance;

namespace RetailPOS.Application.HR.Attendance;

/// <summary>A single employee/shift assignment whose WorkDate is the shift start date.</summary>
public sealed record HrRosterData(long EmployeeId, DateOnly WorkDate, long ShiftId,
    long OperationalLocationProjectionId, string Source = "Manual", string WorkKind = "Normal",
    string DayClassification = "NormalWorkingDay");
/// <summary>A roster and its immutable publication context.</summary>
public sealed record HrRosterResponse(long Id, long BusinessId, HrRosterData Data, string Status, long Version,
    DateTimeOffset? PublishedAtUtc, HrShiftData? PublishedShift, HrRosterLocationSnapshot? PublishedLocation,
    HrDayClassificationResponse? PublishedClassification);
public sealed record HrRosterLocationSnapshot(long ProjectionId, string Name, string SourceSystem,
    string SourceEntityType, string SourceEntityId, long SourceVersion);
public sealed record HrRosterAccessContext(long EmployeeId, long LocationId, IReadOnlyList<long> EmployeePrimaryLocations);

public interface IHrRosterStore
{
    Task<IReadOnlyList<HrRosterResponse>> ListAsync(long business, DateOnly from, DateOnly to, long? employeeId,
        HrOperationalLocationReadScope scope, CancellationToken ct);
    Task<HrRosterAccessContext> AccessContextAsync(long business, long? id, HrRosterData? data, CancellationToken ct);
    Task<HrRosterResponse> SaveAsync(long business, long? id, long? version, HrRosterData data, HrOperationalLocationReadScope scope, CancellationToken ct);
    Task<HrRosterResponse> TransitionAsync(long business, long id, long version, string status, long actor, HrOperationalLocationReadScope scope, CancellationToken ct);
    Task<HrRosterResponse?> PublishedAsync(long business, long employeeId, DateOnly date, CancellationToken ct);
}

public sealed record GetHrRostersQuery(DateOnly From, DateOnly To, long? EmployeeId = null) : IRequest<IReadOnlyList<HrRosterResponse>>;
public sealed record SaveHrRosterCommand(long? Id, long? ExpectedVersion, HrRosterData Data) : IRequest<HrRosterResponse>;
public sealed record BulkAssignHrRosterCommand(IReadOnlyList<HrRosterData> Rows) : IRequest<IReadOnlyList<HrRosterResponse>>;
public sealed record TransitionHrRosterCommand(long Id, long ExpectedVersion, string Status) : IRequest<HrRosterResponse>;
public sealed record GetHrRosterClassificationQuery(long EmployeeId, DateOnly WorkDate) : IRequest<HrDayClassificationResponse>;

public sealed class HrRosterHandlers(IHrOrganisationAccess access, IHrRosterStore store,
    IRequiredAuditTransaction audit) :
    IRequestHandler<GetHrRostersQuery, IReadOnlyList<HrRosterResponse>>,
    IRequestHandler<SaveHrRosterCommand, HrRosterResponse>,
    IRequestHandler<BulkAssignHrRosterCommand, IReadOnlyList<HrRosterResponse>>,
    IRequestHandler<TransitionHrRosterCommand, HrRosterResponse>,
    IRequestHandler<GetHrRosterClassificationQuery, HrDayClassificationResponse>
{
    public async Task<IReadOnlyList<HrRosterResponse>> Handle(GetHrRostersQuery r, CancellationToken ct)
    {
        if (r.From == default || r.To < r.From || r.To.DayNumber - r.From.DayNumber > 366 || r.EmployeeId is <= 0)
            throw new ArgumentException("Provide an inclusive roster range of at most 367 days and valid employee filter.");
        var b = access.RequireBusinessId(); await access.EnsureAsync(b, HrOrganisationOperation.ViewRosters, null, ct);
        return await store.ListAsync(b, r.From, r.To, r.EmployeeId, await access.GetLocationReadScopeAsync(b, ct), ct);
    }
    public async Task<HrRosterResponse> Handle(SaveHrRosterCommand r, CancellationToken ct)
    {
        Validate(r.Data);
        if (r.Id is <= 0 || r.ExpectedVersion is <= 0 || r.Id.HasValue != r.ExpectedVersion.HasValue)
            throw new ArgumentException("Roster identity and expected version must be provided together.");
        var b = access.RequireBusinessId();
        await Ensure(b, r.Id, r.Data, HrOrganisationOperation.ManageRosters, ct);
        if (r.Id.HasValue) await Ensure(b, r.Id, null, HrOrganisationOperation.ManageRosters, ct);
        access.RequireRealUserId();
        var scope = await access.GetLocationReadScopeAsync(b, ct);
        return await audit.ExecuteAsync(new RequiredAuditOperation(b, r.Id.HasValue ? "hr.roster.updated" : "hr.roster.created",
            "HR", "Saved a draft roster.", "HrRosterAssignment", r.Id?.ToString() ?? r.Data.EmployeeId.ToString()),
            t => store.SaveAsync(b, r.Id, r.ExpectedVersion, r.Data, scope, t), ct);
    }
    public async Task<IReadOnlyList<HrRosterResponse>> Handle(BulkAssignHrRosterCommand r, CancellationToken ct)
    {
        if (r.Rows is null || r.Rows.Count is < 1 or > 100 || r.Rows.Select(x => (x.EmployeeId, x.WorkDate)).Distinct().Count() != r.Rows.Count)
            throw new ArgumentException("Bulk roster requires 1-100 distinct employee/date rows.");
        var b = access.RequireBusinessId();
        foreach (var row in r.Rows) { Validate(row); await Ensure(b, null, row, HrOrganisationOperation.ManageRosters, ct); }
        access.RequireRealUserId();
        var scope = await access.GetLocationReadScopeAsync(b, ct);
        return await audit.ExecuteAsync<IReadOnlyList<HrRosterResponse>>(new RequiredAuditOperation(b,
            "hr.roster.bulk_created", "HR", "Created draft rosters atomically.", "HrRosterAssignment", "bulk"),
            async t => {
                var results = new List<HrRosterResponse>();
                foreach (var row in r.Rows.OrderBy(x => x.EmployeeId).ThenBy(x => x.WorkDate))
                    results.Add(await store.SaveAsync(b, null, null, row with { Source = "Bulk" }, scope, t));
                return results;
            }, ct);
    }
    public async Task<HrRosterResponse> Handle(TransitionHrRosterCommand r, CancellationToken ct)
    {
        if (r.Id <= 0 || r.ExpectedVersion <= 0 || r.Status is not ("Published" or "Cancelled"))
            throw new ArgumentException("Valid roster identity, version and transition are required.");
        var b = access.RequireBusinessId(); await Ensure(b, r.Id, null, HrOrganisationOperation.ManageRosters, ct);
        var actor = access.RequireRealUserId();
        var scope = await access.GetLocationReadScopeAsync(b, ct);
        return await audit.ExecuteAsync(new RequiredAuditOperation(b, r.Status == "Published" ? "hr.roster.published" : "hr.roster.cancelled",
            "HR", $"{r.Status} roster.", "HrRosterAssignment", r.Id.ToString()),
            t => store.TransitionAsync(b, r.Id, r.ExpectedVersion, r.Status, actor, scope, t), ct);
    }
    public async Task<HrDayClassificationResponse> Handle(GetHrRosterClassificationQuery r, CancellationToken ct)
    {
        var b = access.RequireBusinessId(); await access.EnsureAsync(b, HrOrganisationOperation.ViewRosters, null, ct);
        var roster = await store.PublishedAsync(b, r.EmployeeId, r.WorkDate, ct)
            ?? throw new KeyNotFoundException("Published roster not found.");
        await access.EnsureLocationAccessibleAsync(b, roster.Data.OperationalLocationProjectionId, ct);
        return roster.PublishedClassification ?? throw new InvalidOperationException("Published roster has no frozen classification.");
    }
    private async Task Ensure(long b, long? id, HrRosterData? data, HrOrganisationOperation operation, CancellationToken ct)
    {
        await access.EnsureAsync(b, operation, data?.OperationalLocationProjectionId, ct);
        var context = await store.AccessContextAsync(b, id, data, ct);
        await access.EnsureLocationAccessibleAsync(b, context.LocationId, ct);
        var scope = await access.GetLocationReadScopeAsync(b, ct);
        if (scope.AllowedSources is not null && context.EmployeePrimaryLocations.Count == 0)
            throw new UnauthorizedAccessException("Employee has no authorized Primary location context.");
        foreach (var location in context.EmployeePrimaryLocations)
            await access.EnsureLocationAccessibleAsync(b, location, ct);
    }
    private static void Validate(HrRosterData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        HrRosterRules.Validate(data.WorkDate, data.EmployeeId, data.ShiftId, data.OperationalLocationProjectionId,
            data.Source, data.WorkKind, data.DayClassification);
    }
}
