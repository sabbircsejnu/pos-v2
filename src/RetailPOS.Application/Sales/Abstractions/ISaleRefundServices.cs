using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Abstractions;

public interface ISaleRefundStore
{
    Task<ISaleVoidTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<Sale?> FindSaleForUpdateAsync(long saleId, CancellationToken cancellationToken);
    Task<string?> FindCurrentSaleStatusAsync(long saleId, CancellationToken cancellationToken);

    /// <summary>Takes the sale row lock. Two refunds of one sale run one after the other, and each sees what the other returned.</summary>
    Task LockSaleAsync(long saleId, CancellationToken cancellationToken);

    /// <summary>Saves the refund so that its identity can go on the journal intent, inside the open transaction.</summary>
    Task AddRefundAsync(RetailPOS.Core.Entities.Uom.SaleRefund refund, CancellationToken cancellationToken);
    Task<RetailPOS.Core.Entities.Inventory?> FindOutletInventoryAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ISaleRefundStockLedgerWriter
{
    void AppendReturn(
        Sale sale,
        SaleItem item,
        decimal quantity,
        decimal balanceAfter,
        long actorUserId,
        string? reason,
        DateTime createdAt);
}

public interface ISaleRefundAuditWriter
{
    Task RecordRefundedAsync(
        Sale sale,
        string previousStatus,
        string? reason,
        decimal refundQuantity,
        long? journalEntryId,
        long actorUserId,
        CancellationToken cancellationToken,
        string? refundNumber = null);
}

public interface ISaleRefundCacheInvalidator
{
    Task InvalidateAsync(long variantId, long outletId, CancellationToken cancellationToken);
}

public interface ISaleRefundLogger
{
    void SaleRefunded(long saleId, string? reason);
}
