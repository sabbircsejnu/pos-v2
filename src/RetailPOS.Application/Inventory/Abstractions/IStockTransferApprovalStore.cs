namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransferApprovalStore
{
    Task<long> ApproveAsync(
        long transferId,
        long businessId,
        long? approvedBy,
        CancellationToken cancellationToken);
}
