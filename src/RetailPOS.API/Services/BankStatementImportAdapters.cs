using RetailPOS.Application.BankStatements.Abstractions;

namespace RetailPOS.API.Services;

public sealed class BankStatementImportTenantScope(ITenantAccessService tenantAccess)
    : IBankStatementImportTenantScope
{
    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }
}

public sealed class BankStatementImportLocationAuthorizer(
    IUserOutletAccessService outletAccess)
    : IBankStatementImportLocationAuthorizer
{
    public async Task AuthorizeAsync(
        long outletId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await outletAccess.ResolveAndAuthorizeLocationAsync(outletId, "outlet");
        cancellationToken.ThrowIfCancellationRequested();
    }
}
