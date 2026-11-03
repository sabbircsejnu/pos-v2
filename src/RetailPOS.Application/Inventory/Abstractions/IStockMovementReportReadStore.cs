using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockMovementReportReadStore
{
    Task<StockMovementReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        StockMovementReportFilter filter,
        CancellationToken cancellationToken);
}
