namespace RetailPOS.API.Services;

public interface IFeatureEntitlementService
{
    Task<bool> IsFeatureEnabledAsync(
        string featureKey,
        long? businessId = null,
        CancellationToken cancellationToken = default);

    Task EnsureFeatureEnabledAsync(
        string featureKey,
        long? businessId = null,
        CancellationToken cancellationToken = default);
}
