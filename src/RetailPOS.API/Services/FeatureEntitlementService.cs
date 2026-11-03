using MediatR;
using RetailPOS.Application.FeatureEntitlements.Queries;

namespace RetailPOS.API.Services;

public class FeatureEntitlementService : IFeatureEntitlementService
{
    private readonly ISender _sender;

    public FeatureEntitlementService(ISender sender)
    {
        _sender = sender;
    }

    public async Task<bool> IsFeatureEnabledAsync(
        string featureKey,
        long? businessId = null,
        CancellationToken cancellationToken = default) =>
        await _sender.Send(
            new IsFeatureEnabledQuery(featureKey, businessId),
            cancellationToken);

    public async Task EnsureFeatureEnabledAsync(
        string featureKey,
        long? businessId = null,
        CancellationToken cancellationToken = default)
    {
        var enabled = await IsFeatureEnabledAsync(featureKey, businessId, cancellationToken);
        if (!enabled)
            throw new UnauthorizedAccessException($"Feature '{featureKey}' is disabled for this business subscription.");
    }
}
