using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.FeatureEntitlements.Abstractions;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.Application.FeatureEntitlements.Queries;

public sealed record IsFeatureEnabledQuery(string FeatureKey, long? BusinessId = null)
    : IQuery<bool>;

public sealed class IsFeatureEnabledQueryHandler(
    IFeatureEntitlementReadStore readStore,
    IFeatureEntitlementScope tenantScope,
    IHrPayrollCapabilityService hrPayrollCapabilities)
    : IRequestHandler<IsFeatureEnabledQuery, bool>
{
    public async Task<bool> Handle(
        IsFeatureEnabledQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FeatureKey))
            return false;

        var normalizedKey = request.FeatureKey.Trim().ToLowerInvariant();
        if (HrPayrollCapabilityKeys.All.Contains(normalizedKey, StringComparer.Ordinal))
        {
            var capabilities = await hrPayrollCapabilities.GetAsync(cancellationToken);
            if (request.BusinessId.HasValue && request.BusinessId.Value != capabilities.BusinessId)
                throw new UnauthorizedAccessException("The requested Business does not match the validated scope.");

            return capabilities.IsEnabled(normalizedKey);
        }

        var businessId = request.BusinessId
            ?? await tenantScope.GetBusinessIdAsync(cancellationToken);
        if (!businessId.HasValue)
            return true;

        return await readStore.IsEnabledAsync(
            businessId.Value,
            normalizedKey,
            cancellationToken);
    }
}
