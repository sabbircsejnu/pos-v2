using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IOutOfStockReportReadStore
{
    Task<OutOfStockReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        OutOfStockReportFilter filter,
        CancellationToken cancellationToken);
}
