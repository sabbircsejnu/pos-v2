using RetailPOS.Application.BusinessAdministration.Abstractions;

namespace RetailPOS.API.Services;

public sealed class BusinessTargetAccessAdapter(
    ITenantAccessService tenantAccess,
    IRoleSwitchContext roleSwitch) : IBusinessTargetAccess
{
    public long RequireTargetBusinessId(long targetBusinessId) =>
        tenantAccess.RequireTargetBusinessId(targetBusinessId);

    public void RequirePlatformSuperAdmin()
    {
        if (!roleSwitch.IsSuperAdmin || roleSwitch.IsRoleSwitched)
            throw new UnauthorizedAccessException("Only a Platform Super Admin can perform this action.");
    }
}
