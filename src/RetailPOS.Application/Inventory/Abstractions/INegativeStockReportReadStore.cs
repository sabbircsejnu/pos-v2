using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface INegativeStockReportReadStore
{
    Task<NegativeStockReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        NegativeStockReportFilter filter,
        CancellationToken cancellationToken);
}
