using RetailPOS.Application.Inventory.Abstractions;

namespace RetailPOS.API.Services;

public sealed class StockTransferReadScopeProvider(ITenantAccessService tenantAccess)
    : IStockTransferReadScopeProvider
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}
