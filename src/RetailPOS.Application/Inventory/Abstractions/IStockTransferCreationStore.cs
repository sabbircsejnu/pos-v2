using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransferCreationStore
{
    Task<long> CreateAsync(
        StockTransferCreateInput input,
        long businessId,
        long? createdBy,
        CancellationToken cancellationToken);
}
