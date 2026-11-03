using RetailPOS.Application.PurchaseOrders.ReadModels;

namespace RetailPOS.Application.PurchaseOrders.Abstractions;

public interface IPurchaseOrderReadStore
{
    Task<decimal> SumTotalAmountAsync(
        string? status,
        DateTime? startDate,
        DateTime? endDate,
        long? businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseOrderReadModel>> FindPendingApprovalsAsync(
        long? businessId,
        CancellationToken cancellationToken);

    Task<PurchaseOrderSearchReadModel> SearchAsync(
        string? status,
        long? supplierId,
        string? locationType,
        long? locationId,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize,
        string sortBy,
        string sortOrder,
        long? businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseOrderReadModel>> FindAllAsync(
        string? status,
        long? supplierId,
        string? locationType,
        long? locationId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<PurchaseOrderReadModel?> FindByIdAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken);
}

public interface IPurchaseOrderTenantScope
{
    Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken);
}
