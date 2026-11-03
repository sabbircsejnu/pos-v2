namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockRequisitionTransferStore
{
    Task<long> ConvertToTransferAsync(
        long requisitionId,
        long businessId,
        long createdBy,
        CancellationToken cancellationToken);
}
