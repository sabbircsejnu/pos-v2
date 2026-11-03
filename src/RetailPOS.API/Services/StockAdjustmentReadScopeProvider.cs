using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.API.Services;

public sealed class StockAdjustmentReadScopeProvider(
    ITenantAccessService tenantAccess,
    IUserOutletAccessService outletAccess) : IStockAdjustmentReadScopeProvider
{
    public async Task<StockAdjustmentReadScope> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorized = await outletAccess.GetAuthorizedOutletsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var locations = authorized.Outlets
            .Select(location => new StockAdjustmentLocationScope(location.Id, "outlet"))
            .Concat(authorized.Warehouses
                .Select(location => new StockAdjustmentLocationScope(location.Id, "warehouse")))
            .ToArray();

        return new StockAdjustmentReadScope(
            tenantAccess.RequireBusinessId(),
            locations);
    }
}
