using RetailPOS.Application.Barcodes.Abstractions;
using RetailPOS.Application.Barcodes.ReadModels;

namespace RetailPOS.API.Services;

public sealed class BarcodeTemplateReadScopeAdapter(ITenantAccessService tenantAccess)
    : IBarcodeTemplateReadScope, IBarcodeTemplateWriteScope
{
    public Task<BarcodeTemplateAccessScope> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BarcodeTemplateAccessScope(
            tenantAccess.EffectiveBusinessId,
            tenantAccess.IsSuperAdmin));
    }
}
