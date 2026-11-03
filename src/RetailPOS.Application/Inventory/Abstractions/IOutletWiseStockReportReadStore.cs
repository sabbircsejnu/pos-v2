using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IOutletWiseStockReportReadStore
{
    Task<OutletWiseStockReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        OutletWiseStockFilter filter,
        CancellationToken cancellationToken);
}
