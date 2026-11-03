namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransferRejectionStore
{
    Task<long> RejectAsync(
        long transferId,
        long businessId,
        string? reason,
        long? rejectedBy,
        CancellationToken cancellationToken);
}
