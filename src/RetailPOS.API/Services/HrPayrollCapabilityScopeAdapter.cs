using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.API.Services;

public sealed class HrPayrollCapabilityScopeAdapter(ITenantAccessService tenantAccess)
    : IHrPayrollCapabilityScope
{
    public long RequireBusinessId() => tenantAccess.RequireBusinessId();
}
