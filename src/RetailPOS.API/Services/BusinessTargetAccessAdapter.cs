using RetailPOS.Application.BusinessAdministration.Abstractions;

namespace RetailPOS.API.Services;

public sealed class BusinessTargetAccessAdapter(ITenantAccessService tenantAccess) : IBusinessTargetAccess
{
    public long RequireTargetBusinessId(long targetBusinessId) =>
        tenantAccess.RequireTargetBusinessId(targetBusinessId);
}
