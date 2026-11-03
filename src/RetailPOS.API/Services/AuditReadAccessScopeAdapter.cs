using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.API.Services;

public sealed class AuditReadAccessScopeAdapter(
    ITenantAccessService tenant,
    IRoleSwitchContext role,
    IHttpContextAccessor http,
    IHrPayrollCapabilityService capabilities) : IAuditReadAccessScope
{
    public async Task<AuditReadAccess> GetAsync(CancellationToken cancellationToken)
    {
        var businessId = tenant.EffectiveBusinessId;
        if (businessId is not > 0)
            return new AuditReadAccess(null, null, false, false, false);
        var flags = await capabilities.GetAsync(cancellationToken);
        if (flags.BusinessId != businessId)
            throw new UnauthorizedAccessException("Audit Business scope mismatch.");
        bool Has(string permission) => http.HttpContext?.User.Claims.Any(claim =>
            claim.Type == "permission" && (claim.Value == "*" || claim.Value == permission)) == true;

        // Foundation gate is deliberately conservative for mixed events; fine-grained process ACLs follow in HR slices.
        return new AuditReadAccess(businessId, role.EffectiveOutletId,
            role.IsSuperAdmin || role.IsBusinessOwner,
            flags.HrEnabled && Has("audit.view") && Has("hr.employee.view_sensitive") && Has("hr.employee.view_identity")
                && Has("hr.employee_document.view"),
            flags.PayrollEnabled && Has("audit.view") && Has("payroll.salary.view") && Has("payroll.payment.view_sensitive")
                && Has("payroll.payslip.view") && Has("payroll.export"));
    }
}
