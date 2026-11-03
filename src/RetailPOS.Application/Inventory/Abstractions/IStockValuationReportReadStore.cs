using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockValuationReportReadStore
{
    Task<StockValuationReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        StockValuationReportFilter filter,
        CancellationToken cancellationToken);
}
