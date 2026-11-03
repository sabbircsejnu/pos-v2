using RetailPOS.Application.Abstractions;

namespace RetailPOS.API.Services;

public sealed class BusinessScopeAdapter(ITenantAccessService tenantAccess) : IBusinessScope
{
    public long? EffectiveBusinessId => tenantAccess.EffectiveBusinessId;

    public long RequireBusinessId() => tenantAccess.RequireBusinessId();
}
