using RetailPOS.API.Authorization;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.API.Services;

public sealed class InventoryReportReadScopeProvider(
    ITenantAccessService tenantAccess,
    IUserOutletAccessService outletAccess) : IInventoryReportReadScopeProvider
{
    public async Task<InventoryReportReadScope> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorized = await outletAccess.GetAuthorizedOutletsAsync();
        long? businessId = tenantAccess.RequireBusinessId();

        return new InventoryReportReadScope(
            businessId,
            authorized.Outlets.Select(location => location.Id).ToArray(),
            authorized.Warehouses.Select(location => location.Id).ToArray());
    }
}
