using RetailPOS.Application.Authorization;

namespace RetailPOS.API.Services;

public class TenantAccessService : ITenantAccessService
{
    private readonly IRoleSwitchContext _roleSwitch;

    public TenantAccessService(IRoleSwitchContext roleSwitch)
    {
        _roleSwitch = roleSwitch;
    }

    public bool IsSuperAdmin => _roleSwitch.IsSuperAdmin;
    public long? EffectiveBusinessId => _roleSwitch.EffectiveBusinessId;

    public long RequireBusinessId()
    {
        var businessId = EffectiveBusinessId;
        if (!businessId.HasValue || businessId.Value <= 0)
            throw new BusinessContextRequiredException();

        return businessId.Value;
    }

    public void EnsurePlatformAdministrator()
    {
        if (!IsSuperAdmin)
            throw new UnauthorizedAccessException("Platform administration requires a Super Admin.");
    }

    public long RequireTargetBusinessId(long targetBusinessId)
    {
        if (targetBusinessId <= 0)
            throw new ArgumentException("A positive target Business identifier is required.", nameof(targetBusinessId));

        if (IsSuperAdmin)
            return targetBusinessId;

        if (RequireBusinessId() != targetBusinessId)
            throw new UnauthorizedAccessException("The target Business is outside your authorized scope.");

        return targetBusinessId;
    }

    public void EnsureBusinessMatch(long? entityBusinessId, string resourceName)
    {
        var scopeBusinessId = RequireBusinessId();
        if (!entityBusinessId.HasValue || entityBusinessId.Value != scopeBusinessId)
            throw new UnauthorizedAccessException($"You are not authorized to access this {resourceName}.");
    }
}
