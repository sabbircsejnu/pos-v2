using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Abstractions;

public interface ISaleVoidStore
{
    Task<ISaleVoidTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    /// <summary>Locks the sale row (FOR UPDATE on PostgreSQL) — taken before any cash or drawer lock (sale → session → drawer).</summary>
    Task LockSaleAsync(long saleId, CancellationToken cancellationToken);
    Task<Sale?> FindSaleForUpdateAsync(long saleId, CancellationToken cancellationToken);
    Task<string?> FindCurrentSaleStatusAsync(long saleId, CancellationToken cancellationToken);
    Task<bool> TryMarkCompletedSaleVoidedAsync(long saleId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the committed state: true when any line of the sale has a returned quantity (refund or exchange).
    /// Called after the sale row is held by the void's own update, so a concurrent exchange is already visible.
    /// </summary>
    Task<bool> HasReturnedQuantityAsync(long saleId, CancellationToken cancellationToken);
    Task<RetailPOS.Core.Entities.Inventory?> FindOutletInventoryAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken);
    Task<Customer?> FindCustomerForUpdateAsync(long customerId, CancellationToken cancellationToken);
    Task AddSaleVoidAsync(SaleVoid saleVoid, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ISaleMutationOutletAuthorizer
{
    Task AuthorizeAsync(long outletId, CancellationToken cancellationToken);
}

public interface ISaleVoidTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

/// <param name="RestoredCost">Value actually returned to inventory, when a condition was assessed. Null means the whole cost.</param>
public sealed record SaleRefundLine(long SaleItemId, long VariantId, decimal Quantity, decimal? RestoredCost = null);

public interface ISaleVoidStockLedgerWriter
{
    void AppendReturn(
        Sale sale,
        SaleItem item,
        decimal balanceAfter,
        long actorUserId,
        string reason,
        DateTime createdAt);
}

public interface ISaleVoidAuditWriter
{
    Task RecordVoidedAsync(
        Sale sale,
        string previousStatus,
        string reason,
        long? journalEntryId,
        long actorUserId,
        CancellationToken cancellationToken);
}

public interface ISaleVoidCacheInvalidator
{
    Task InvalidateAsync(long variantId, long outletId, CancellationToken cancellationToken);
}

public interface ISaleVoidLogger
{
    void SaleVoided(long saleId, string reason);
}
