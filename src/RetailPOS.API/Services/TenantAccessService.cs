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
        if (!businessId.HasValue)
            throw new UnauthorizedAccessException("Business scope is required for this operation.");

        return businessId.Value;
    }

    public void EnsureBusinessMatch(long? entityBusinessId, string resourceName)
    {
        if (IsSuperAdmin)
            return;

        var scopeBusinessId = RequireBusinessId();
        if (!entityBusinessId.HasValue || entityBusinessId.Value != scopeBusinessId)
            throw new UnauthorizedAccessException($"You are not authorized to access this {resourceName}.");
    }
}
