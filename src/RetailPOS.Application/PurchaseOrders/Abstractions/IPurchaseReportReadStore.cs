using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Abstractions;

public interface IPurchaseReportReadStore
{
    Task<PurchaseSummaryReadModel> GetSummaryAsync(
        DateTime startDate,
        DateTime endDate,
        long? businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseBySupplierReadModel>> GetBySupplierAsync(
        DateTime startDate,
        DateTime endDate,
        long? businessId,
        CancellationToken cancellationToken);
}
