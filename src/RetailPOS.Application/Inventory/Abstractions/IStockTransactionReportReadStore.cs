using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransactionReportReadStore
{
    Task<StockTransactionReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        StockTransactionReportFilter filter,
        CancellationToken cancellationToken);
}
