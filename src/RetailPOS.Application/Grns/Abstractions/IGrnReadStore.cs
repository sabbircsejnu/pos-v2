using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnReadStore
{
    Task<GrnReadModel?> FindByIdAsync(
        long grnId,
        GrnReadScope scope,
        CancellationToken cancellationToken);

    Task<GrnVarianceReadModel?> FindVarianceByIdAsync(
        long grnId,
        GrnReadScope scope,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GrnReadModel>> FindAllAsync(
        long? purchaseOrderId,
        string? status,
        GrnReadScope scope,
        CancellationToken cancellationToken);

    Task<GrnSearchReadModel> SearchAsync(
        GrnSearchCriteria criteria,
        GrnReadScope scope,
        CancellationToken cancellationToken);
}

public interface IGrnTenantScope
{
    Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken);
}
