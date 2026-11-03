using MediatR;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.Application.HR.Dashboard;

public sealed record HrDashboardQuery(
    DateOnly From,
    DateOnly To,
    long? LocationProjectionId,
    string? LocationType)
    : IRequest<HrDashboardSummary>;

public sealed record HrDashboardMetric(string Label, long Count, long? Minutes = null);

public sealed record HrDashboardSummary(
    DateOnly From,
    DateOnly To,
    long TotalEmployees,
    IReadOnlyList<HrDashboardMetric> EmploymentStatuses,
    IReadOnlyList<HrDashboardMetric> EmploymentTypes,
    IReadOnlyList<HrDashboardMetric> LocationTypes,
    IReadOnlyList<HrDashboardMetric> Locations,
    IReadOnlyList<HrDashboardMetric> OrgUnits,
    IReadOnlyList<HrDashboardMetric> Positions,
    long Joiners,
    long Exits,
    IReadOnlyList<HrDashboardMetric> Attendance,
    IReadOnlyList<HrDashboardMetric> LeaveRequests,
    IReadOnlyList<HrDashboardMetric> Overtime,
    long DocumentsExpiring,
    long OpenExitCases,
    long PendingMandatoryClearanceItems);

public interface IHrDashboardStore
{
    Task<HrDashboardSummary> QueryAsync(
        long businessId,
        HrDashboardQuery request,
        HrOperationalLocationReadScope scope,
        bool attendanceEnabled,
        CancellationToken cancellationToken);
}

public sealed class HrDashboardHandler(
    IHrOrganisationAccess organisationAccess,
    IHrOrganisationReadStore organisationReadStore,
    IHrPayrollCapabilityService capabilities,
    IHrDashboardStore store) : IRequestHandler<HrDashboardQuery, HrDashboardSummary>
{
    public async Task<HrDashboardSummary> Handle(
        HrDashboardQuery request,
        CancellationToken cancellationToken)
    {
        if (request.From == default || request.To < request.From
            || request.To.DayNumber - request.From.DayNumber > 366
            || request.LocationProjectionId is <= 0
            || request.LocationType is not null
                and not ("Outlet" or "Warehouse"))
            throw new ArgumentException("HR dashboard date range or location is invalid.");

        var businessId = organisationAccess.RequireBusinessId();
        var enabledCapabilities = await capabilities.GetAsync(cancellationToken);
        if (!enabledCapabilities.HrEnabled)
            throw new UnauthorizedAccessException("HR capability is disabled.");

        var scope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
        if (request.LocationProjectionId is long locationId
            && !await organisationReadStore.IsLocationAccessibleAsync(
                businessId, locationId, scope, cancellationToken))
            throw new UnauthorizedAccessException("The dashboard location is outside the caller's scope.");

        return await store.QueryAsync(
            businessId, request, scope, enabledCapabilities.AttendanceEnabled, cancellationToken);
    }
}
