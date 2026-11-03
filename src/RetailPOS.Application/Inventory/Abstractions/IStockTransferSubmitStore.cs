namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransferSubmitStore
{
    Task<long> SubmitAsync(
        long transferId,
        long businessId,
        long? submittedBy,
        CancellationToken cancellationToken);
}
