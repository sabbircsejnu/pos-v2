namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

public interface IMobileStockCountOperationStore
{
    Task<MobileStockCountOperationsBatchResult> ApplyBatchAsync(
        ApplyMobileStockCountOperationsInput input,
        CancellationToken cancellationToken);

    Task<MobileStockCountOperationsPageReadModel?> GetAfterRevisionAsync(
        long businessId,
        long outletId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId,
        long afterRevision,
        CancellationToken cancellationToken);

    Task<MobileStockCountOperationReceiptReadModel?> FindReceiptAsync(
        long businessId,
        long outletId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId,
        Guid clientOperationId,
        CancellationToken cancellationToken);
}
