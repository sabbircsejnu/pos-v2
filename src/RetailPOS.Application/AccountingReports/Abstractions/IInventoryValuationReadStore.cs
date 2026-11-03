using RetailPOS.Application.AccountingReports.ReadModels;

namespace RetailPOS.Application.AccountingReports.Abstractions;

public interface IInventoryValuationReadStore
{
    Task<InventoryValuationReadModel> GetAsync(
        InventoryValuationFilter filter,
        long businessId,
        CancellationToken cancellationToken);
}
