using RetailPOS.Application.SupplierPayments.Abstractions;

namespace RetailPOS.API.Services;

public sealed class SupplierPaymentCreationAuthorizer(
    ITenantAccessService tenantAccess,
    IUserOutletAccessService? outletAccess = null)
    : ISupplierPaymentCreationAuthorizer, ISupplierPaymentWorkflowAuthorizer
{
    public async Task<SupplierPaymentCreationScope> AuthorizeAsync(
        long? outletId,
        CancellationToken cancellationToken)
    {
        var businessId = await GetBusinessIdAsync(cancellationToken);
        var authorizedOutletId = await EnforceOutletAsync(outletId, cancellationToken);
        return new SupplierPaymentCreationScope(businessId, authorizedOutletId);
    }

    public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(tenantAccess.RequireBusinessId());
    }

    public async Task<long?> EnforceOutletAsync(
        long? outletId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorizedOutletId = outletAccess is null
            ? outletId
            : await outletAccess.EnforceWriteOutletAsync(outletId);
        cancellationToken.ThrowIfCancellationRequested();
        return authorizedOutletId;
    }
}
