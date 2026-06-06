namespace RetailPOS.API.Services;

public interface ITenantAccessService
{
    bool IsSuperAdmin { get; }
    long? EffectiveBusinessId { get; }

    long RequireBusinessId();
    void EnsureBusinessMatch(long? entityBusinessId, string resourceName);
}
