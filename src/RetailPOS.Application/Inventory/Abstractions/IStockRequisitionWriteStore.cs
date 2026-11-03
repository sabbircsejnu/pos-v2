using RetailPOS.Application.Inventory.StockRequisitions;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockRequisitionWriteStore
{
    Task<long> CreateAsync(
        StockRequisitionWriteInput input,
        long businessId,
        long requestedBy,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        long id,
        StockRequisitionWriteInput input,
        long businessId,
        long updatedBy,
        CancellationToken cancellationToken);
}
