using RetailPOS.Application.AccountingSettlements.Abstractions;

namespace RetailPOS.API.Services;

public sealed class SettlementTransferTenantScopeAdapter(ITenantAccessService tenantAccess)
    : ISettlementTransferTenantScope
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}
