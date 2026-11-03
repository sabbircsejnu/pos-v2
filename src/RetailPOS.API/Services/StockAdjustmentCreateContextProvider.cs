using MediatR;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Application.Settings.Queries;

namespace RetailPOS.API.Services;

public sealed class StockAdjustmentCreateContextProvider(
    ITenantAccessService tenantAccess,
    IUserOutletAccessService outletAccess,
    ISender sender) : IStockAdjustmentCreateContextProvider
{
    public async Task<StockAdjustmentCreateContext> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorized = await outletAccess.GetAuthorizedOutletsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var settings = (await sender.Send(new GetSystemSettingsQuery(), cancellationToken)).Inventory;
        var locations = authorized.Outlets
            .Select(location => new StockAdjustmentLocationScope(location.Id, "outlet"))
            .Concat(authorized.Warehouses
                .Select(location => new StockAdjustmentLocationScope(location.Id, "warehouse")))
            .ToArray();

        return new StockAdjustmentCreateContext(
            new StockAdjustmentReadScope(tenantAccess.RequireBusinessId(), locations),
            settings.AllowNegativeStock);
    }
}
