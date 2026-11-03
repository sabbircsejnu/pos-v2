using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;

namespace RetailPOS.API.Services;

public sealed class BarcodePrintHistoryReadScopeAdapter(ITenantAccessService tenantAccess)
    : IBarcodePrintHistoryReadScope, IBarcodePrintHistoryWriteScope
{
    public Task<BarcodePrintHistoryAccessScope> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BarcodePrintHistoryAccessScope(
            tenantAccess.EffectiveBusinessId,
            tenantAccess.IsSuperAdmin));
    }
}
