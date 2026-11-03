using RetailPOS.Application.Barcodes.Abstractions;

namespace RetailPOS.API.Services;

public sealed class BarcodeTenantScopeAdapter(ITenantAccessService tenantAccess)
    : IBarcodeTenantScope
{
    public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.EffectiveBusinessId);
    }
}
