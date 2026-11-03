using RetailPOS.Application.Inventory.ReadModels;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IInventoryReportReadScopeProvider
{
    Task<InventoryReportReadScope> GetAsync(CancellationToken cancellationToken);
}
