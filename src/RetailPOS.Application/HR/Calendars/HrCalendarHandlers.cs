using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Calendars;

namespace RetailPOS.Application.HR.Calendars;

public sealed record GetHrShiftsQuery(string? Search = null) : IRequest<IReadOnlyList<HrShiftResponse>>;
public sealed record SaveHrShiftCommand(long? Id, long? ExpectedVersion, HrShiftData? Data) : IRequest<HrShiftResponse>;
public sealed record GetHrHolidayCalendarsQuery : IRequest<IReadOnlyList<HrHolidayCalendarResponse>>;
public sealed record SaveHrHolidayCalendarCommand(long? Id, long? ExpectedVersion,
    HrHolidayCalendarData? Data, DateOnly? EffectiveTo = null) : IRequest<HrHolidayCalendarResponse>;
public sealed record GetHrHolidaysQuery(long CalendarId) : IRequest<IReadOnlyList<HrHolidayResponse>>;
public sealed record SaveHrHolidayCommand(long CalendarId, long? Id, long? ExpectedVersion,
    HrHolidayData? Data) : IRequest<HrHolidayResponse>;
public sealed record GetHrWeeklyOffQuery : IRequest<IReadOnlyList<HrWeeklyOffResponse>>;
public sealed record SaveHrWeeklyOffCommand(long? PreviousId, long? ExpectedVersion,
    HrWeeklyOffData? Data, DateOnly? EffectiveTo = null) : IRequest<HrWeeklyOffResponse>;
public sealed record GetHrDayClassificationQuery(DateOnly WorkDate, long LocationId) : IRequest<HrDayClassificationResponse>;

public sealed class HrCalendarHandlers(IHrOrganisationAccess access, IHrCalendarStore store,
    IRequiredAuditTransaction audit, TimeProvider clock) :
    IRequestHandler<GetHrShiftsQuery, IReadOnlyList<HrShiftResponse>>,
    IRequestHandler<SaveHrShiftCommand, HrShiftResponse>,
    IRequestHandler<GetHrHolidayCalendarsQuery, IReadOnlyList<HrHolidayCalendarResponse>>,
    IRequestHandler<SaveHrHolidayCalendarCommand, HrHolidayCalendarResponse>,
    IRequestHandler<GetHrHolidaysQuery, IReadOnlyList<HrHolidayResponse>>,
    IRequestHandler<SaveHrHolidayCommand, HrHolidayResponse>,
    IRequestHandler<GetHrWeeklyOffQuery, IReadOnlyList<HrWeeklyOffResponse>>,
    IRequestHandler<SaveHrWeeklyOffCommand, HrWeeklyOffResponse>,
    IRequestHandler<GetHrDayClassificationQuery, HrDayClassificationResponse>
{
    public async Task<IReadOnlyList<HrShiftResponse>> Handle(GetHrShiftsQuery request, CancellationToken ct)
    {
        var business = await Ensure(HrOrganisationOperation.ViewShifts, null, false, ct);
        return await store.ShiftsAsync(business, request.Search, ct);
    }

    public async Task<HrShiftResponse> Handle(SaveHrShiftCommand request, CancellationToken ct)
    {
        Identity(request.Id, request.ExpectedVersion, request.Data is null);
        if (request.Data is { } data)
        {
            Text(data.Code, 50, "Shift code"); Text(data.Name, 100, "Shift name");
            ArgumentNullException.ThrowIfNull(data.Breaks);
            HrShiftRules.Validate(new HrShift
            {
                StartTimeLocal = data.StartTimeLocal, EndTimeLocal = data.EndTimeLocal,
                CrossesMidnight = data.CrossesMidnight, MinimumWorkMinutes = data.MinimumWorkMinutes,
                MaximumAllowedWorkMinutes = data.MaximumAllowedWorkMinutes,
                AllowedEarlyPunchMinutes = data.AllowedEarlyPunchMinutes,
                AllowedLatePunchMinutes = data.AllowedLatePunchMinutes,
                MinimumRestBeforeNextShiftMinutes = data.MinimumRestBeforeNextShiftMinutes,
                GraceInMinutes = data.GraceInMinutes, GraceOutMinutes = data.GraceOutMinutes,
                Breaks = data.Breaks.Select(b => new HrShiftBreak
                    { Sequence = b.Sequence, DurationMinutes = b.DurationMinutes, IsPaid = b.IsPaid }).ToList()
            });
        }
        var business = await Ensure(HrOrganisationOperation.ManageShifts, null, true, ct);
        return await Write(business, "HrShift", request.Id, request.Data is null ? "deactivate" : "save",
            token => store.SaveShiftAsync(business, request.Id, request.ExpectedVersion, request.Data, token), ct);
    }

    public async Task<IReadOnlyList<HrHolidayCalendarResponse>> Handle(GetHrHolidayCalendarsQuery request, CancellationToken ct)
    {
        var business = await Ensure(HrOrganisationOperation.ViewHolidays, null, false, ct);
        return await store.CalendarsAsync(business, await access.GetLocationReadScopeAsync(business, ct), ct);
    }

    public async Task<HrHolidayCalendarResponse> Handle(SaveHrHolidayCalendarCommand request, CancellationToken ct)
    {
        Identity(request.Id, request.ExpectedVersion, request.Data is null);
        if (request.Data is { } data)
        {
            Text(data.Name, 150, "Calendar name"); ValidateScope(data.Scope);
            Dates(data.EffectiveFrom, data.EffectiveTo);
        }
        else Future(request.EffectiveTo ?? throw new ArgumentException("Effective end date is required."));
        var business = access.RequireBusinessId();
        if (request.Id.HasValue)
            await EnsureScope(HrOrganisationOperation.ManageHolidays,
                await store.CalendarScopeAsync(business, request.Id.Value, ct), true, ct);
        await EnsureScope(HrOrganisationOperation.ManageHolidays, request.Data?.Scope, true, ct);
        return await Write(business, "HrHolidayCalendar", request.Id, request.Data is null ? "deactivate" : "save",
            token => store.SaveCalendarAsync(business, request.Id, request.ExpectedVersion,
                request.Data, request.EffectiveTo, token), ct);
    }

    public async Task<IReadOnlyList<HrHolidayResponse>> Handle(GetHrHolidaysQuery request, CancellationToken ct)
    {
        var business = access.RequireBusinessId();
        await EnsureScope(HrOrganisationOperation.ViewHolidays,
            await store.CalendarScopeAsync(business, request.CalendarId, ct), false, ct);
        return await store.HolidaysAsync(business, request.CalendarId, ct);
    }

    public async Task<HrHolidayResponse> Handle(SaveHrHolidayCommand request, CancellationToken ct)
    {
        Identity(request.Id, request.ExpectedVersion, request.Data is null);
        if (request.Data is { } data)
        {
            Future(data.HolidayDate); Text(data.Code, 50, "Holiday code"); Text(data.Name, 150, "Holiday name");
            Text(data.HolidayType, 50, "Holiday classification");
            if (data.Notes?.Length > 1000) throw new ArgumentException("Holiday notes exceed 1000 characters.");
        }
        var business = access.RequireBusinessId();
        await EnsureScope(HrOrganisationOperation.ManageHolidays,
            await store.CalendarScopeAsync(business, request.CalendarId, ct), true, ct);
        return await Write(business, "HrHoliday", request.Id, request.Data is null ? "deactivate" : "save",
            token => store.SaveHolidayAsync(business, request.CalendarId, request.Id, request.ExpectedVersion,
                request.Data, token), ct);
    }

    public async Task<IReadOnlyList<HrWeeklyOffResponse>> Handle(GetHrWeeklyOffQuery request, CancellationToken ct)
    {
        var business = await Ensure(HrOrganisationOperation.ViewWeeklyOff, null, false, ct);
        return await store.WeeklyOffAsync(business, await access.GetLocationReadScopeAsync(business, ct), ct);
    }

    public async Task<HrWeeklyOffResponse> Handle(SaveHrWeeklyOffCommand request, CancellationToken ct)
    {
        Identity(request.PreviousId, request.ExpectedVersion, request.Data is null);
        if (request.Data is { } data)
        {
            ValidateScope(data.Scope); Dates(data.EffectiveFrom, data.EffectiveTo);
            if (data.Days is null || data.Days.Count == 0 || data.Days.Count > 7
                || data.Days.Distinct().Count() != data.Days.Count || data.Days.Any(d => !Enum.IsDefined(d)))
                throw new ArgumentException("Select distinct valid weekly-off days.");
        }
        else Future(request.EffectiveTo ?? throw new ArgumentException("Effective end date is required."));
        var business = access.RequireBusinessId();
        if (request.PreviousId.HasValue)
            await EnsureScope(HrOrganisationOperation.ManageWeeklyOff,
                await store.WeeklyOffScopeAsync(business, request.PreviousId.Value, ct), true, ct);
        if (request.Data is not null)
            await EnsureScope(HrOrganisationOperation.ManageWeeklyOff, request.Data.Scope, true, ct);
        return await Write(business, "HrWeeklyOffPolicyVersion", request.PreviousId,
            request.Data is null ? "deactivate" : request.PreviousId.HasValue ? "version" : "create",
            token => store.SaveWeeklyOffAsync(business, request.PreviousId, request.ExpectedVersion,
                request.Data, request.EffectiveTo, token), ct);
    }

    public async Task<HrDayClassificationResponse> Handle(GetHrDayClassificationQuery request, CancellationToken ct)
    {
        if (request.WorkDate == default || request.LocationId <= 0)
            throw new ArgumentException("Work date and HR operational location are required.");
        var scope = new HrCalendarScope("Location", null, request.LocationId);
        var business = await EnsureScope(HrOrganisationOperation.ViewHolidays, scope, false, ct);
        await EnsureScope(HrOrganisationOperation.ViewWeeklyOff, scope, false, ct);
        return await store.ClassifyAsync(business, request.WorkDate, request.LocationId, ct);
    }

    private async Task<long> EnsureScope(HrOrganisationOperation operation, HrCalendarScope? scope, bool write, CancellationToken ct) =>
        await Ensure(operation, scope?.OperationalLocationProjectionId, write && scope?.ScopeType != "Location", ct);

    private async Task<long> Ensure(HrOrganisationOperation operation, long? location, bool businessWide, CancellationToken ct)
    {
        var business = access.RequireBusinessId();
        await access.EnsureAsync(business, operation, location, ct);
        if (businessWide && (await access.GetLocationReadScopeAsync(business, ct)).AllowedSources is not null)
            throw new UnauthorizedAccessException("Business-wide configuration requires Business-wide location access.");
        return business;
    }

    private Task<T> Write<T>(long business, string entity, long? id, string action, Func<CancellationToken, Task<T>> write, CancellationToken ct)
    {
        access.RequireRealUserId();
        return audit.ExecuteAsync(new RequiredAuditOperation(business, action, "hr",
            $"{action} {entity}", entity, id?.ToString() ?? "new"), write, ct);
    }

    private void Future(DateOnly date)
    {
        if (date <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            throw new ArgumentException("Calendar changes must take effect after the current UTC calendar date.");
    }
    private void Dates(DateOnly from, DateOnly? to)
    {
        Future(from);
        if (to < from) throw new ArgumentException("Effective end cannot precede effective start.");
    }
    internal static void Text(string value, int limit, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > limit)
            throw new ArgumentException($"{field} is required and must not exceed {limit} characters.");
    }
    internal static void Identity(long? id, long? version, bool deactivating)
    {
        if (id is <= 0 || version is <= 0 || id.HasValue != version.HasValue || deactivating && !id.HasValue)
            throw new ArgumentException("Identity and expected concurrency version must be supplied together.");
    }
    internal static void ValidateScope(HrCalendarScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.ScopeType == "Company" && scope.LocationType is null && scope.OperationalLocationProjectionId is null
            || scope.ScopeType == "LocationType" && scope.LocationType is "Outlet" or "Warehouse" or "Factory"
                && scope.OperationalLocationProjectionId is null
            || scope.ScopeType == "Location" && scope.LocationType is null && scope.OperationalLocationProjectionId is > 0)
            return;
        throw new ArgumentException("Scope must be Company, a supported LocationType, or one HR operational location.");
    }
}
