using RetailPOS.Application.Sales.Abstractions;

namespace RetailPOS.API.Services;

public sealed class HeldSaleOutletAuthorizerAdapter(IUserOutletAccessService outletAccess)
    : IHeldSaleOutletAuthorizer
{
    public async Task<long> AuthorizeReadAsync(long outletId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (authorizedOutletId, locationType) =
            await outletAccess.ResolveAndAuthorizeLocationAsync(outletId, "outlet");
        cancellationToken.ThrowIfCancellationRequested();

        if (locationType != "outlet" || authorizedOutletId != outletId)
            throw new UnauthorizedAccessException("The requested location is not an authorized Outlet.");
        return outletId;
    }

    public async Task<long> AuthorizeWriteAsync(long outletId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorizedOutletId = await outletAccess.EnforceWriteOutletAsync(outletId);
        cancellationToken.ThrowIfCancellationRequested();
        return authorizedOutletId;
    }

    public async Task<IReadOnlyList<long>> GetAuthorizedOutletIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorization = await outletAccess.GetAuthorizedOutletsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return authorization.Outlets.Select(outlet => outlet.Id).ToArray();
    }
}
