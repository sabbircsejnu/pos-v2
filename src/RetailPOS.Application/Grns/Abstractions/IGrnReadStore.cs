using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnReadStore
{
    Task<GrnReadModel?> FindByIdAsync(
        long grnId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<GrnVarianceReadModel?> FindVarianceByIdAsync(
        long grnId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GrnReadModel>> FindAllAsync(
        long? purchaseOrderId,
        string? status,
        long? businessId,
        CancellationToken cancellationToken);

    Task<GrnSearchReadModel> SearchAsync(
        long? purchaseOrderId,
        string? status,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize,
        long? businessId,
        CancellationToken cancellationToken);
}

public interface IGrnTenantScope
{
    Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken);
}
