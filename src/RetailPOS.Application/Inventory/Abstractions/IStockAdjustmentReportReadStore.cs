using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockAdjustmentReportReadStore
{
    Task<StockAdjustmentReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        StockAdjustmentReportFilter filter,
        CancellationToken cancellationToken);
}
