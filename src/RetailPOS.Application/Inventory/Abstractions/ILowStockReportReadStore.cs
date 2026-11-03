using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface ILowStockReportReadStore
{
    Task<LowStockReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        LowStockReportFilter filter,
        CancellationToken cancellationToken);
}
