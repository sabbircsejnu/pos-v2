using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransferReportReadStore
{
    Task<StockTransferReportReadModel> GetAsync(
        StockTransferReadScope scope,
        StockTransferReportFilter filter,
        CancellationToken cancellationToken);
}
