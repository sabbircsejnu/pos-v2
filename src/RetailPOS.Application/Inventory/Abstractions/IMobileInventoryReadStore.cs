using RetailPOS.Application.Inventory.ReadModels.Mobile;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IMobileInventoryReadStore
{
    Task<MobileInventoryPageReadModel> SearchOutletStockAsync(
        MobileInventoryCriteria criteria,
        CancellationToken cancellationToken);
}
