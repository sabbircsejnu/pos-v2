using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransferDraftStore
{
    Task<long> UpdateDraftAsync(
        long transferId,
        StockTransferCreateInput input,
        long businessId,
        CancellationToken cancellationToken);
}
