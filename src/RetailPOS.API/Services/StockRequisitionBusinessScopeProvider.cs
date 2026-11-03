using RetailPOS.Application.Inventory.Abstractions;

namespace RetailPOS.API.Services;

public sealed class StockRequisitionBusinessScopeProvider(ITenantAccessService tenantAccess)
    : IStockRequisitionBusinessScopeProvider
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}
