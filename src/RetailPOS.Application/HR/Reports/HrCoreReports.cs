using MediatR;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.Application.HR.Reports;

public static class HrCoreReportLimits
{
    public const int MaximumExportRows = 5000;
}

public sealed record HrCoreReportQuery(
    string Kind,
    DateOnly From,
    DateOnly To,
    long? EmployeeId = null,
    long? LocationProjectionId = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 50,
    DateOnly? AsOfDate = null,
    long? EmploymentTypeId = null,
    string? EmploymentStatus = null,
    long? PositionId = null,
    long? DepartmentId = null,
    string? SortBy = null,
    bool Descending = true) : IRequest<HrCoreReportPage>;

public sealed record HrCoreReportMeasures(
    int? ScheduledDays = null,
    int? PresentDays = null,
    int? AbsentDays = null,
    int? LateDays = null,
    int? HolidayDays = null,
    int? WeeklyOffDays = null,
    decimal? ApprovedLeaveUnits = null,
    int? WorkedMinutes = null,
    int? ApprovedOvertimeEntries = null,
    decimal? Opening = null,
    decimal? Accrued = null,
    decimal? Used = null,
    decimal? Restored = null,
    decimal? Adjusted = null,
    decimal? CarriedForward = null,
    decimal? Expired = null,
    decimal? Balance = null,
    int? MandatoryClearanceItems = null,
    int? CompletedMandatoryClearanceItems = null);

public sealed record HrCoreReportRow(
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string Category,
    DateOnly? EffectiveDate,
    string Status,
    string? LocationName,
    string? Detail,
    int? Metric,
    string? EmploymentType = null,
    string? Department = null,
    string? Position = null,
    string? LocationType = null,
    DateOnly? EffectiveTo = null,
    bool? IsCurrent = null,
    long? ActorUserId = null,
    DateTime? OccurredAtUtc = null,
    decimal? Quantity = null,
    string? CompletionMode = null,
    bool? PayrollEnabled = null,
    HrCoreReportMeasures? Measures = null);

public sealed record HrCoreReportPage(
    string Kind,
    IReadOnlyList<HrCoreReportRow> Items,
    int Page,
    int PageSize,
    long TotalCount);

public interface IHrCoreReportStore
{
    Task<HrCoreReportPage> QueryAsync(
        long businessId,
        HrCoreReportQuery request,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken);
}

public sealed class HrCoreReportHandler(
    IHrOrganisationAccess organisationAccess,
    IHrOrganisationReadStore organisationReadStore,
    IHrPayrollCapabilityService capabilities,
    IHrCoreReportStore store) : IRequestHandler<HrCoreReportQuery, HrCoreReportPage>
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "EmployeeMaster", "EmployeeRoster", "Lifecycle", "LifecycleHistory",
        "LocationAssignmentHistory",         "PositionAssignmentHistory", "AttendanceSummary", "AttendanceExceptions", "AttendanceCorrections",
        "LeaveSummary", "LeaveBalance", "OvertimeSummary", "DocumentExpiry", "ExitClearance"
    };

    public async Task<HrCoreReportPage> Handle(HrCoreReportQuery request, CancellationToken cancellationToken)
    {
        if (!Kinds.Contains(request.Kind)
            || request.From == default || request.To < request.From
            || request.To.DayNumber - request.From.DayNumber > 366
            || request.EmployeeId is <= 0
            || request.LocationProjectionId is <= 0
            || request.EmploymentTypeId is <= 0
            || request.PositionId is <= 0
            || request.DepartmentId is <= 0
            || request.AsOfDate is DateOnly asOfDate
                && (asOfDate < request.From || asOfDate > request.To)
            || request.Page < 1 || request.PageSize is < 1 or > 200
            || (long)(request.Page - 1) * request.PageSize > int.MaxValue
            || request.Status?.Length > 40
            || request.EmploymentStatus?.Length > 40
            || request.SortBy is not null && request.SortBy is not (
                "employeeCode" or "employeeName" or "effectiveDate" or "category" or "status" or "locationName"))
            throw new ArgumentException("Core HR report filters or paging values are invalid.");

        var businessId = organisationAccess.RequireBusinessId();
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        if (request.Kind is "AttendanceSummary" or "AttendanceExceptions" or "AttendanceCorrections")
            await organisationAccess.EnsureAsync(businessId,
                HrOrganisationOperation.ViewAttendanceReports, request.LocationProjectionId, cancellationToken);
        var scope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
        if (request.LocationProjectionId is long locationId
            && !await organisationReadStore.IsLocationAccessibleAsync(
                businessId, locationId, scope, cancellationToken))
            throw new UnauthorizedAccessException("The report location is outside the caller's scope.");

        return await store.QueryAsync(businessId, request, scope, cancellationToken);
    }
}
