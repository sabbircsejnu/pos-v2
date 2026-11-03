using RetailPOS.Application.Pricing.Abstractions;

namespace RetailPOS.API.Services;

public sealed class OutletPriceOverrideWriteAuthorizerAdapter(
    IUserOutletAccessService outletAccess)
    : IOutletPriceOverrideWriteAuthorizer
{
    public async Task<long> AuthorizeWriteAsync(
        long outletId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorizedOutletId = await outletAccess.EnforceWriteOutletAsync(outletId);
        cancellationToken.ThrowIfCancellationRequested();
        return authorizedOutletId;
    }
}
