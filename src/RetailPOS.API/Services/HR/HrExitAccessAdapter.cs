using System.Security.Claims;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Entities.HR.Exit;

namespace RetailPOS.API.Services.HR;

public sealed class HrExitAccessAdapter(
    IHttpContextAccessor httpContextAccessor,
    IHrOrganisationAccess organisationAccess,
    IHrOrganisationReadStore organisationReadStore,
    IHrPayrollCapabilityService capabilities,
    ITenantAccessService tenantAccess,
    IHrEmployeeLocationAssignmentStore employeeLocations) : IHrExitAccess
{
    public long RequireBusinessId()
    {
        var businessId = organisationAccess.RequireBusinessId();
        if (tenantAccess.RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Exit access is outside the active Business.");
        return businessId;
    }

    public long RequireRealUserId() => organisationAccess.RequireRealUserId();

    public async Task EnsureAsync(
        long businessId, HrExitOperation operation, long? employeeId, long? locationId,
        CancellationToken cancellationToken)
    {
        if (businessId <= 0 || RequireBusinessId() != businessId)
            throw new UnauthorizedAccessException("Exit access is outside the active Business.");
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        if (operation is HrExitOperation.FinalSettlementView
            or HrExitOperation.FinalSettlementCalculate
            or HrExitOperation.FinalSettlementSubmit
            or HrExitOperation.FinalSettlementApprove
            or HrExitOperation.FinalSettlementReject
            or HrExitOperation.FinalSettlementCancel
            or HrExitOperation.FinalSettlementFinancialView
            or HrExitOperation.FinalSettlementAccountingPost
            or HrExitOperation.FinalSettlementPaymentCreate
            or HrExitOperation.FinalSettlementPaymentView
            or HrExitOperation.FinalSettlementPaymentHold
            or HrExitOperation.FinalSettlementPaymentRelease
            or HrExitOperation.FinalSettlementPaymentProcess
            or HrExitOperation.FinalSettlementPaymentFail
            or HrExitOperation.FinalSettlementPaymentReturn
            or HrExitOperation.FinalSettlementPaymentRetry
            or HrExitOperation.FinalSettlementPaymentAccountingPost)
            await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
        var required = operation switch
        {
            HrExitOperation.View => "hr.exit.view",
            HrExitOperation.Initiate => "hr.exit.initiate",
            HrExitOperation.Approve => "hr.exit.approve",
            HrExitOperation.Clearance => "hr.exit.clearance",
            HrExitOperation.FinalSettlementView => "payroll.final_settlement.view",
            HrExitOperation.FinalSettlementCalculate => "payroll.final_settlement.calculate",
            HrExitOperation.FinalSettlementSubmit => "payroll.final_settlement.submit",
            HrExitOperation.FinalSettlementApprove => "payroll.final_settlement.approve",
            HrExitOperation.FinalSettlementReject => "payroll.final_settlement.reject",
            HrExitOperation.FinalSettlementCancel => "payroll.final_settlement.cancel",
            HrExitOperation.FinalSettlementFinancialView => "payroll.final_settlement.financial.view",
            HrExitOperation.FinalSettlementAccountingPost => "payroll.final_settlement.accounting.post",
            HrExitOperation.FinalSettlementPaymentCreate => "payroll.final_settlement.payment.create",
            HrExitOperation.FinalSettlementPaymentView => "payroll.final_settlement.payment.view",
            HrExitOperation.FinalSettlementPaymentHold => "payroll.final_settlement.payment.hold",
            HrExitOperation.FinalSettlementPaymentRelease => "payroll.final_settlement.payment.release",
            HrExitOperation.FinalSettlementPaymentProcess => "payroll.final_settlement.payment.process",
            HrExitOperation.FinalSettlementPaymentFail => "payroll.final_settlement.payment.fail",
            HrExitOperation.FinalSettlementPaymentReturn => "payroll.final_settlement.payment.return",
            HrExitOperation.FinalSettlementPaymentRetry => "payroll.final_settlement.payment.retry",
            HrExitOperation.FinalSettlementPaymentAccountingPost =>
                "payroll.final_settlement.accounting.post",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        if (operation is HrExitOperation.FinalSettlementApprove
            or HrExitOperation.FinalSettlementReject
            or HrExitOperation.FinalSettlementCancel)
            required = operation switch
            {
                HrExitOperation.FinalSettlementApprove => "payroll.final_settlement.approve",
                HrExitOperation.FinalSettlementReject => "payroll.final_settlement.reject",
                HrExitOperation.FinalSettlementCancel => "payroll.final_settlement.cancel",
                _ => required
            };
        if (!HasPermission(httpContextAccessor.HttpContext?.User, required))
            throw new UnauthorizedAccessException("The caller lacks the required exit permission.");
        if (operation == HrExitOperation.FinalSettlementView
            && !HasPermission(httpContextAccessor.HttpContext?.User, "payroll.salary.view"))
            throw new UnauthorizedAccessException(
                "The caller lacks permission to view confidential salary information.");
        if (operation is HrExitOperation.FinalSettlementFinancialView
                or HrExitOperation.FinalSettlementPaymentView
            && !HasPermission(httpContextAccessor.HttpContext?.User, "payroll.salary.view"))
            throw new UnauthorizedAccessException(
                "The caller lacks permission to view confidential salary information.");

        if (locationId is > 0)
            await organisationAccess.EnsureLocationAccessibleAsync(
                businessId, locationId.Value, cancellationToken);
        if (employeeId is > 0)
        {
            var scope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
            if (!await employeeLocations.IsEmployeeAccessibleAsync(
                    businessId, employeeId.Value, scope.AllowedSources, cancellationToken))
                throw new UnauthorizedAccessException(
                    "Employee is outside the caller's Primary location scope.");
        }
    }

    public async Task EnsureTemplateScopeAsync(
        long businessId, HrClearanceTemplateScopeType scopeType, string? scopeReference,
        CancellationToken cancellationToken)
    {
        switch (scopeType)
        {
            case HrClearanceTemplateScopeType.Business:
                return;
            case HrClearanceTemplateScopeType.Location:
                if (!long.TryParse(scopeReference, out var locationId) || locationId <= 0)
                    throw new ArgumentException("A positive location scope reference is required.");
                await EnsureAsync(businessId, HrExitOperation.Clearance, null, locationId, cancellationToken);
                return;
            case HrClearanceTemplateScopeType.LocationType:
                if (scopeReference is not ("Outlet" or "Warehouse"))
                    throw new ArgumentException(
                        "Only currently projected operational location types can be configured.");
                var scope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
                var locations = await organisationReadStore.GetOperationalLocationsAsync(
                    businessId, scopeReference, null, scope, cancellationToken);
                if (locations.Count == 0)
                    throw new UnauthorizedAccessException(
                        "No accessible operational locations match the clearance template scope.");
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(scopeType));
        }
    }

    public async Task<IReadOnlyCollection<long>?> GetAccessibleLocationIdsAsync(
        long businessId, CancellationToken cancellationToken)
    {
        var scope = await organisationAccess.GetLocationReadScopeAsync(businessId, cancellationToken);
        return (await organisationReadStore.GetOperationalLocationsAsync(
            businessId, null, null, scope, cancellationToken))
            .Select(location => location.Id).ToArray();
    }

    private static bool HasPermission(ClaimsPrincipal? principal, string permission)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return false;
        var accepted = PermissionCatalog.ExpandClaims(permission);
        return principal.Claims.Any(claim => claim.Type == "permission"
            && (claim.Value == "*" || accepted.Contains(claim.Value, StringComparer.OrdinalIgnoreCase)));
    }
}
