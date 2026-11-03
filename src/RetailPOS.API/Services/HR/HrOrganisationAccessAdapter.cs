using System.Security.Claims;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Audit;

namespace RetailPOS.API.Services.HR;

public sealed class HrOrganisationAccessAdapter(
    IHttpContextAccessor httpContextAccessor,
    IAuditContext auditContext,
    IHrPayrollCapabilityService capabilities,
    ITenantAccessService tenantAccess,
    IUserOutletAccessService locationAccess,
    IHrOrganisationReadStore readStore,
    IHrEmployeeLocationAssignmentStore employeeLocations,
    IHrEmployeeDocumentStore documentStore,
    IHrEmployeeDocumentReadStore documentReadStore,
    IHrEmployeeStore employees,
    IHrLeaveRequestStore leaveRequests) : IHrOrganisationAccess, IHrEmployeeDocumentAccess,
        IConfidentialFileAccess
{
    public long RequireBusinessId()
    {
        if (!auditContext.IsInitialized || auditContext.BusinessId is not > 0)
            throw new BusinessContextRequiredException("A validated Business scope is required.");
        return auditContext.BusinessId.Value;
    }

    public long RequireRealUserId()
    {
        if (!auditContext.IsInitialized || auditContext.RealUserId is not > 0)
            throw new UnauthorizedAccessException("An authenticated real user is required.");
        return auditContext.RealUserId.Value;
    }

    public async Task EnsureAsync(
        long businessId,
        HrOrganisationOperation operation,
        long? locationProjectionId,
        CancellationToken cancellationToken)
    {
        if (businessId <= 0 || RequireBusinessId() != businessId
            || tenantAccess.RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("HR organisation access is outside the active Business.");
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        if (operation is HrOrganisationOperation.ViewDevices or HrOrganisationOperation.ManageDevices
            or HrOrganisationOperation.ViewDeviceMappings or HrOrganisationOperation.ManageDeviceMappings)
            await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.DeviceIntegration, cancellationToken);
        if (operation is HrOrganisationOperation.ViewShifts or HrOrganisationOperation.ManageShifts
            or HrOrganisationOperation.ViewHolidays or HrOrganisationOperation.ManageHolidays
            or HrOrganisationOperation.ViewWeeklyOff or HrOrganisationOperation.ManageWeeklyOff
            or HrOrganisationOperation.ViewRosters or HrOrganisationOperation.ManageRosters
            or HrOrganisationOperation.ViewAttendance or HrOrganisationOperation.ViewAttendanceReports
            or HrOrganisationOperation.CreateManualAttendancePunch
            or HrOrganisationOperation.ViewRawAttendancePunches or HrOrganisationOperation.ProcessAttendance
            or HrOrganisationOperation.CorrectAttendance)
            await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Attendance, cancellationToken);
        var permission = operation switch
        {
            HrOrganisationOperation.ViewOrgUnits => "hr.organisation.view",
            HrOrganisationOperation.ManageOrgUnits => "hr.organisation.manage",
            HrOrganisationOperation.ViewOperationalLocations => "hr.location.view",
            HrOrganisationOperation.ReconcileOperationalLocations => "hr.location.manage",
            HrOrganisationOperation.ViewPositions => "hr.position.view",
            HrOrganisationOperation.ManagePositions => "hr.position.manage",
            HrOrganisationOperation.ViewEmployees => "hr.employee.view",
            HrOrganisationOperation.CreateEmployees => "hr.employee.create",
            HrOrganisationOperation.EditEmployees => "hr.employee.edit",
            HrOrganisationOperation.ViewEmployeeLifecycle => "hr.employee_lifecycle.view",
            HrOrganisationOperation.ManageEmployeeLifecycle => "hr.employee_lifecycle.manage",
            HrOrganisationOperation.RehireEmployee => "hr.employee_lifecycle.rehire",
            HrOrganisationOperation.ManageEmployeePositionAssignments => "hr.position.manage",
            HrOrganisationOperation.LinkEmployeeUser => "hr.employee.link_user",
            HrOrganisationOperation.UnlinkEmployeeUser => "hr.employee.unlink_user",
            HrOrganisationOperation.ResolveCurrentEmployee => null,
            HrOrganisationOperation.ViewEmployeeLocationAssignments => "hr.employee_transfer.view",
            HrOrganisationOperation.ManageEmployeeLocationAssignments => "hr.employee_transfer.create",
            HrOrganisationOperation.RequestEmployeeTransfer => "hr.employee_transfer.create",
            HrOrganisationOperation.ApproveEmployeeTransfer => "hr.employee_transfer.approve",
            HrOrganisationOperation.CancelEmployeeTransfer => "hr.employee_transfer.create",
            HrOrganisationOperation.ImportEmployees => "hr.import.execute",
            HrOrganisationOperation.ViewImports => "hr.import.view",
            HrOrganisationOperation.ViewShifts => "hr.shift.view",
            HrOrganisationOperation.ManageShifts => "hr.shift.manage",
            HrOrganisationOperation.ViewHolidays => "hr.holiday.view",
            HrOrganisationOperation.ManageHolidays => "hr.holiday.manage",
            HrOrganisationOperation.ViewWeeklyOff => "hr.weekly_off.view",
            HrOrganisationOperation.ManageWeeklyOff => "hr.weekly_off.manage",
            HrOrganisationOperation.ViewRosters => "hr.roster.view",
            HrOrganisationOperation.ManageRosters => "hr.roster.manage",
            HrOrganisationOperation.ViewDevices => "hr.device.view",
            HrOrganisationOperation.ManageDevices => "hr.device.manage",
            HrOrganisationOperation.ViewDeviceMappings => "hr.device_user_mapping.view",
            HrOrganisationOperation.ManageDeviceMappings => "hr.device_user_mapping.manage",
            HrOrganisationOperation.ViewAttendance => "hr.attendance.view",
            HrOrganisationOperation.ViewAttendanceReports => "hr.attendance.report",
            HrOrganisationOperation.CreateManualAttendancePunch => "hr.attendance.manual_entry",
            HrOrganisationOperation.ViewRawAttendancePunches => "hr.attendance.raw_punch.view",
            HrOrganisationOperation.ProcessAttendance => "hr.attendance.reprocess",
            HrOrganisationOperation.CorrectAttendance => "hr.attendance.correct",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || (permission is not null
                && !HasPermission(principal, PermissionCatalog.ExpandClaims(permission))))
            throw new UnauthorizedAccessException("The caller lacks the required HR organisation permission.");
        if (operation == HrOrganisationOperation.ResolveCurrentEmployee)
            return;

        var scope = await GetLocationReadScopeAsync(businessId, cancellationToken);
        if (locationProjectionId.HasValue
            && !await readStore.IsLocationAccessibleAsync(businessId, locationProjectionId.Value, scope,
                cancellationToken))
            throw new UnauthorizedAccessException("The requested operational location is outside the caller's location scope.");
        if (operation == HrOrganisationOperation.ReconcileOperationalLocations
            && scope.AllowedSources is not null)
            throw new UnauthorizedAccessException("Projection reconciliation requires Business-wide location access.");
    }

    public async Task<HrOperationalLocationReadScope> GetLocationReadScopeAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        if (RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Operational location read is outside the active Business.");
        var authorized = await locationAccess.GetAuthorizedOutletsAsync();
        if (authorized.IsGlobalAccess)
            return new HrOperationalLocationReadScope(businessId, null);

        var sources = authorized.Outlets.Select(row => new OperationalLocationSourceIdentity("Outlet", row.Id.ToString()))
            .Concat(authorized.Warehouses.Select(row =>
                new OperationalLocationSourceIdentity("Warehouse", row.Id.ToString())))
            .ToArray();
        return new HrOperationalLocationReadScope(businessId, sources);
    }

    public async Task EnsureLocationAccessibleAsync(
        long businessId,
        long locationProjectionId,
        CancellationToken cancellationToken)
    {
        if (locationProjectionId <= 0 || RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Operational location access is outside the active Business.");
        var scope = await GetLocationReadScopeAsync(businessId, cancellationToken);
        if (!await readStore.IsLocationAccessibleAsync(businessId, locationProjectionId, scope, cancellationToken))
            throw new UnauthorizedAccessException("The operational location is outside the caller's location scope.");
    }

    async Task IHrEmployeeDocumentAccess.EnsureAsync(
        long businessId,
        long? employeeId,
        HrEmployeeDocumentOperation operation,
        string? classification,
        CancellationToken cancellationToken)
    {
        if (businessId <= 0 || RequireBusinessId() != businessId
            || tenantAccess.RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Employee document access is outside the active Business.");
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        var permission = operation switch
        {
            HrEmployeeDocumentOperation.View or HrEmployeeDocumentOperation.ViewTypes => "hr.employee_document.view",
            HrEmployeeDocumentOperation.Upload => "hr.employee_document.upload",
            HrEmployeeDocumentOperation.Replace => "hr.employee_document.replace",
            HrEmployeeDocumentOperation.Download => "hr.employee_document.download",
            HrEmployeeDocumentOperation.Archive => "hr.employee_document.delete",
            HrEmployeeDocumentOperation.ManageTypes => "hr.settings.manage",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || !HasPermission(principal, PermissionCatalog.ExpandClaims(permission)))
            throw new UnauthorizedAccessException("The caller lacks the required employee document permission.");
        if (classification is "Confidential" or "Restricted"
            && !HasPermission(principal, PermissionCatalog.ExpandClaims("hr.employee.view_sensitive")))
            throw new UnauthorizedAccessException("The caller lacks permission for this document classification.");
        if (employeeId is { } id)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(employeeId));
            var scope = await GetLocationReadScopeAsync(businessId, cancellationToken);
            if (!await employeeLocations.IsEmployeeAccessibleAsync(
                businessId, id, scope.AllowedSources, cancellationToken))
                throw new UnauthorizedAccessException("The employee is outside the caller's location scope.");
        }
    }

    async Task IConfidentialFileAccess.RequireAsync(
        long businessId,
        long subjectId,
        ConfidentialFileClassification classification,
        string action,
        CancellationToken cancellationToken,
        Guid? versionId)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new UnauthorizedAccessException("Confidential file classification or action is invalid.");
        if (classification == ConfidentialFileClassification.EmployeeDocument)
        {
            var document = await documentStore.GetAsync(businessId, subjectId, cancellationToken)
                ?? throw new UnauthorizedAccessException("Employee document is not accessible.");
            await ((IHrEmployeeDocumentAccess)this).EnsureAsync(
                businessId, document.EmployeeId, action.Equals("download", StringComparison.OrdinalIgnoreCase)
                    ? HrEmployeeDocumentOperation.Download
                    : HrEmployeeDocumentOperation.Upload,
                document.Classification, cancellationToken);
            if (action.Equals("download", StringComparison.OrdinalIgnoreCase)
                && (versionId is null || !await documentReadStore.IsCurrentDownloadableVersionAsync(
                    businessId, subjectId, versionId.Value, cancellationToken)))
                throw new UnauthorizedAccessException("Only the current clean employee document version can be downloaded.");
            return;
        }
        if (classification != ConfidentialFileClassification.LeaveRequestAttachment
            || !action.Equals("upload", StringComparison.OrdinalIgnoreCase)
                && !action.Equals("download", StringComparison.OrdinalIgnoreCase)
            || RequireBusinessId() != businessId || tenantAccess.RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Leave attachment classification, action or Business is invalid.");

        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Attendance, cancellationToken);
        var request = await leaveRequests.GetAsync(businessId, subjectId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Leave request is not accessible.");
        var currentEmployee = await employees.GetCurrentEmployeeAsync(
            businessId, RequireRealUserId(), cancellationToken);
        var isRequester = currentEmployee?.Id == request.EmployeeId
            && request.RequestedByUserId == RequireRealUserId();
        var principal = httpContextAccessor.HttpContext?.User;
        var canView = principal?.Identity?.IsAuthenticated == true
            && HasPermission(principal, PermissionCatalog.ExpandClaims("hr.leave.view"));
        if (action.Equals("upload", StringComparison.OrdinalIgnoreCase)
            && (!isRequester || request.Status != "Draft"))
            throw new UnauthorizedAccessException("Only the requester may attach files to their own draft.");
        if (action.Equals("download", StringComparison.OrdinalIgnoreCase) && !isRequester && !canView)
            throw new UnauthorizedAccessException("The caller cannot read this leave attachment.");

        var scope = await GetLocationReadScopeAsync(businessId, cancellationToken);
        if (!await employeeLocations.IsEmployeeAccessibleAsync(
                businessId, request.EmployeeId, scope.AllowedSources, cancellationToken))
            throw new UnauthorizedAccessException("Leave attachment is outside the caller's Primary location scope.");
        await EnsureLocationAccessibleAsync(
            businessId, request.ScopeLocationProjectionId, cancellationToken);
        if (action.Equals("download", StringComparison.OrdinalIgnoreCase)
            && (versionId is null || !await leaveRequests.IsCurrentCleanAttachmentAsync(
                businessId, request.Id, versionId.Value, cancellationToken)))
            throw new UnauthorizedAccessException("Only the current clean leave attachment can be downloaded.");
    }

    private static bool HasPermission(ClaimsPrincipal principal, IReadOnlyCollection<string> accepted) =>
        principal.Claims.Any(claim => claim.Type == "permission"
            && (claim.Value == "*" || accepted.Contains(claim.Value, StringComparer.OrdinalIgnoreCase)));
}
