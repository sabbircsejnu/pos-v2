using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IProductLedgerReportReadStore
{
    Task<ProductLedgerReportReadModel> GetAsync(
        InventoryReportReadScope scope,
        ProductLedgerReportFilter filter,
        CancellationToken cancellationToken);
}
