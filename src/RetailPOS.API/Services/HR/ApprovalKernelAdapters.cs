using System.Security.Claims;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.API.Services.HR;

public sealed class ApprovalKernelIdentityAdapter(IAuditContext context) : IApprovalKernelIdentity
{
    public long RequireBusinessId()
    {
        if (!context.IsInitialized || context.BusinessId is not > 0)
            throw new BusinessContextRequiredException("A validated Business scope is required.");
        return context.BusinessId.Value;
    }

    public long RequireRealUserId()
    {
        if (!context.IsInitialized || context.RealUserId is not > 0)
            throw new UnauthorizedAccessException("A real authenticated user is required.");
        return context.RealUserId.Value;
    }

    public long? ActingRoleId => context.ActingRoleId;

    public Guid RequireCorrelationId()
    {
        if (!context.IsInitialized || context.CorrelationId == Guid.Empty)
            throw new UnauthorizedAccessException("A correlation identity is required.");
        return context.CorrelationId;
    }
}

public sealed class ApprovalKernelAccessAdapter(
    IHttpContextAccessor httpContextAccessor,
    IAuditContext auditContext,
    IHrPayrollCapabilityService capabilities,
    IHrOrganisationAccess organisationAccess,
    IHrOrganisationReadStore organisationReadStore) : IApprovalKernelAccess
{
    public async Task EnsureAsync(
        long businessId,
        ApprovalProcessType processType,
        ApprovalKernelOperation operation,
        ApprovalScopeType scopeType,
        string? scopeReference,
        CancellationToken cancellationToken)
    {
        if (!auditContext.IsInitialized || auditContext.BusinessId != businessId)
            throw new UnauthorizedAccessException("Approval request is outside the authenticated Business scope.");
        if (operation == ApprovalKernelOperation.ManagePolicy
            && (processType is ApprovalProcessType.SalarySetup
                or ApprovalProcessType.SalaryRevision
                or ApprovalProcessType.EmployeeLoan
                or ApprovalProcessType.PayrollRun
                or ApprovalProcessType.FinalSettlement)
            && scopeType != ApprovalScopeType.Business)
            throw new UnauthorizedAccessException(
                "Payroll approval policies must use Business scope.");
        if (scopeType == ApprovalScopeType.Location)
        {
            if (processType is not (ApprovalProcessType.Leave or ApprovalProcessType.Overtime
                    or ApprovalProcessType.EmployeeTransfer or ApprovalProcessType.Exit
                    or ApprovalProcessType.AttendanceCorrection or ApprovalProcessType.SalarySetup
                    or ApprovalProcessType.SalaryRevision or ApprovalProcessType.EmployeeLoan
                    or ApprovalProcessType.PayrollRun or ApprovalProcessType.FinalSettlement)
                || !long.TryParse(scopeReference, out var locationProjectionId)
                || locationProjectionId <= 0)
                throw new UnauthorizedAccessException("This approval process has an unsupported location scope.");
            await organisationAccess.EnsureLocationAccessibleAsync(
                businessId, locationProjectionId, cancellationToken);
        }
        else if (scopeType == ApprovalScopeType.LocationType && processType == ApprovalProcessType.Exit)
        {
            if (scopeReference is not ("Outlet" or "Warehouse"))
                throw new UnauthorizedAccessException("This exit approval location type is not supported.");
            var locationScope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
            var locations = await organisationReadStore.GetOperationalLocationsAsync(
                businessId, scopeReference, null, locationScope, cancellationToken);
            if (locations.Count == 0)
                throw new UnauthorizedAccessException(
                    "No accessible operational locations match this exit approval scope.");
        }
        else if (scopeType != ApprovalScopeType.Business || !string.IsNullOrWhiteSpace(scopeReference))
            throw new UnauthorizedAccessException("This approval process requires a supported Business or location scope.");
        if ((processType is ApprovalProcessType.EmployeeTransfer or ApprovalProcessType.AttendanceCorrection)
            && scopeType != ApprovalScopeType.Location)
            throw new UnauthorizedAccessException("This approval process requires an authorized operational location scope.");

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("An authenticated user is required for Approval Kernel access.");

        var requiredCapability = processType switch
        {
            ApprovalProcessType.SalarySetup or ApprovalProcessType.SalaryRevision
                or ApprovalProcessType.EmployeeLoan or ApprovalProcessType.PayrollRun
                or ApprovalProcessType.FinalSettlement
                => HrPayrollCapabilityKeys.Payroll,
            ApprovalProcessType.AttendanceCorrection => HrPayrollCapabilityKeys.Attendance,
            _ => HrPayrollCapabilityKeys.Hr
        };
        await capabilities.EnsureEnabledAsync(requiredCapability, cancellationToken);
        if (processType == ApprovalProcessType.EmployeeLoan)
            await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        if (processType == ApprovalProcessType.Overtime)
            await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Attendance, cancellationToken);

        var permission = GetPermission(processType, operation);
        var accepted = PermissionCatalog.ExpandClaims(permission);
        if (!HasPermission(httpContext.User, accepted))
            throw new UnauthorizedAccessException("The caller lacks the process-specific approval permission.");
    }

    private static string GetPermission(ApprovalProcessType processType, ApprovalKernelOperation operation) =>
        (processType, operation) switch
        {
            (_, ApprovalKernelOperation.ManagePolicy) when processType is ApprovalProcessType.SalarySetup
                or ApprovalProcessType.SalaryRevision or ApprovalProcessType.EmployeeLoan
                or ApprovalProcessType.PayrollRun or ApprovalProcessType.FinalSettlement
                => "payroll.approval_policy.manage",
            (_, ApprovalKernelOperation.ManagePolicy) => "hr.approval_policy.manage",
            (ApprovalProcessType.Leave, ApprovalKernelOperation.Submit) => "hr.leave.request",
            (ApprovalProcessType.Overtime, ApprovalKernelOperation.Submit) => "hr.overtime.submit",
            (ApprovalProcessType.EmployeeTransfer, ApprovalKernelOperation.Submit) => "hr.employee_transfer.create",
            (ApprovalProcessType.AttendanceCorrection, ApprovalKernelOperation.Submit) => "hr.attendance.correct",
            (ApprovalProcessType.SalarySetup, ApprovalKernelOperation.Submit) => "payroll.salary_setup.submit",
            (ApprovalProcessType.SalaryRevision, ApprovalKernelOperation.Submit) => "payroll.salary_revision.submit",
            (ApprovalProcessType.EmployeeLoan, ApprovalKernelOperation.Submit) => "payroll.loan.manage",
            (ApprovalProcessType.Exit, ApprovalKernelOperation.Submit) => "hr.exit.initiate",
            (ApprovalProcessType.PayrollRun, ApprovalKernelOperation.Submit) => "payroll.run.submit",
            (ApprovalProcessType.FinalSettlement, ApprovalKernelOperation.Submit) => "payroll.final_settlement.submit",
            (ApprovalProcessType.Leave, ApprovalKernelOperation.Cancel) => "hr.leave.request",
            (ApprovalProcessType.Overtime, ApprovalKernelOperation.Cancel) => "hr.overtime.submit",
            (ApprovalProcessType.EmployeeTransfer, ApprovalKernelOperation.Cancel) => "hr.employee_transfer.create",
            (ApprovalProcessType.AttendanceCorrection, ApprovalKernelOperation.Cancel) => "hr.attendance.correct",
            (ApprovalProcessType.SalarySetup, ApprovalKernelOperation.Cancel) => "payroll.salary_setup.submit",
            (ApprovalProcessType.SalaryRevision, ApprovalKernelOperation.Cancel) => "payroll.salary_revision.submit",
            (ApprovalProcessType.EmployeeLoan, ApprovalKernelOperation.Cancel) => "payroll.loan.manage",
            (ApprovalProcessType.Exit, ApprovalKernelOperation.Cancel) => "hr.exit.initiate",
            (ApprovalProcessType.PayrollRun, ApprovalKernelOperation.Cancel) => "payroll.run.submit",
            (ApprovalProcessType.FinalSettlement, ApprovalKernelOperation.Cancel) => "payroll.final_settlement.cancel",
            (ApprovalProcessType.Leave, _) => "hr.leave.approve",
            (ApprovalProcessType.Overtime, _) => "hr.overtime.approve",
            (ApprovalProcessType.EmployeeTransfer, _) => "hr.employee_transfer.approve",
            (ApprovalProcessType.AttendanceCorrection, _) => "hr.attendance.approve_correction",
            (ApprovalProcessType.SalarySetup, _) => "payroll.salary_setup.approve",
            (ApprovalProcessType.SalaryRevision, _) => "payroll.salary_revision.approve",
            (ApprovalProcessType.EmployeeLoan, _) => "payroll.loan.approve",
            (ApprovalProcessType.Exit, _) => "hr.exit.approve",
            (ApprovalProcessType.PayrollRun, _) => "payroll.run.approve",
            (ApprovalProcessType.FinalSettlement, ApprovalKernelOperation.Reject) => "payroll.final_settlement.reject",
            (ApprovalProcessType.FinalSettlement, _) => "payroll.final_settlement.approve",
            _ => throw new ArgumentOutOfRangeException(nameof(processType))
        };

    private static bool HasPermission(ClaimsPrincipal principal, IReadOnlyCollection<string> accepted) =>
        principal.Claims.Any(claim => claim.Type == "permission"
            && (claim.Value == "*" || accepted.Contains(claim.Value, StringComparer.OrdinalIgnoreCase)));
}

public sealed class UnconfiguredApprovalCandidateResolver : IApprovalCandidateResolver
{
    public Task<IReadOnlyCollection<long>> ResolveAsync(
        long businessId,
        ApprovalPolicy policy,
        ApprovalPolicyStep step,
        long requesterUserId,
        ApprovalScopeType scopeType,
        string? scopeReference,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<long>>(Array.Empty<long>());
}

public sealed class UnconfiguredApprovalTargetSnapshotReader : IApprovalTargetSnapshotReader
{
    public Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken) =>
        Task.FromResult<ApprovalTargetSnapshot?>(null);
}
