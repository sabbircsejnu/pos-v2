namespace RetailPOS.API.Services;

public interface IFeatureEntitlementService
{
    Task<bool> IsFeatureEnabledAsync(string featureKey, long? businessId = null);
    Task EnsureFeatureEnabledAsync(string featureKey, long? businessId = null);
}
