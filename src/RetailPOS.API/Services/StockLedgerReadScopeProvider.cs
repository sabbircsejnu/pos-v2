using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockLedger;

namespace RetailPOS.API.Services;

public sealed class StockLedgerReadScopeProvider(
    ITenantAccessService tenantAccess,
    IUserOutletAccessService outletAccess) : IStockLedgerReadScopeProvider
{
    public async Task<StockLedgerReadScope> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorized = await outletAccess.GetAuthorizedOutletsAsync();
        var locations = authorized.Outlets
            .Select(location => new StockLedgerLocationScope(location.Id, "outlet"))
            .Concat(authorized.Warehouses
                .Select(location => new StockLedgerLocationScope(location.Id, "warehouse")))
            .ToArray();

        return new StockLedgerReadScope(
            tenantAccess.RequireBusinessId(),
            locations);
    }
}
