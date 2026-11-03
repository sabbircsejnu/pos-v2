namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransferCancellationStore
{
    Task<long> CancelAsync(
        long transferId,
        long businessId,
        string? reason,
        long? cancelledBy,
        CancellationToken cancellationToken);
}
