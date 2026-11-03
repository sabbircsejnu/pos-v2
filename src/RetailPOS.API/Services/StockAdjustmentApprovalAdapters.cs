using RetailPOS.Application.Inventory.Abstractions;

namespace RetailPOS.API.Services;

public sealed class StockAdjustmentPostingAccountResolver(
    IAccountingConfigurationService accountingConfiguration,
    ITenantAccessService tenantAccess,
    RetailPOS.Core.Audit.IAuditContext? audit = null)
    : IStockAdjustmentPostingAccountResolver
{
    public async Task<long> ResolveAccountIdAsync(
        long businessId,
        string slot,
        long? outletId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IntegrationJobBusinessScope.RequireBusinessId(tenantAccess, audit) != businessId)
        {
            throw new UnauthorizedAccessException(
                "The active Business does not match the stock adjustment approval scope.");
        }

        var resolution = await accountingConfiguration.ResolvePostingAccountAsync(slot, outletId);
        cancellationToken.ThrowIfCancellationRequested();
        return resolution.AccountId;
    }
}

public sealed class StockAdjustmentApprovalCacheInvalidator(
    IPosCacheService posCache)
    : IStockAdjustmentApprovalCacheInvalidator
{
    public async Task InvalidateAsync(
        long locationId,
        IReadOnlyList<long> variantIds,
        CancellationToken cancellationToken)
    {
        foreach (var variantId in variantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await posCache.InvalidateStockAsync(variantId, locationId);
        }
    }
}
