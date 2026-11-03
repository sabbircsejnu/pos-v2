using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.API.Services;

public sealed class JournalAttachmentTenantScope(ITenantAccessService tenantAccess)
    : IJournalAttachmentTenantScope
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}
