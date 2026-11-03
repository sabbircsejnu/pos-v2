namespace RetailPOS.Application.FeatureEntitlements.Abstractions;

public interface IFeatureEntitlementReadStore
{
    Task<bool> IsEnabledAsync(
        long businessId,
        string normalizedFeatureKey,
        CancellationToken cancellationToken);
}

public interface IFeatureEntitlementScope
{
    Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken);
}
