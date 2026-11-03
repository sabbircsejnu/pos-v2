namespace RetailPOS.Application.Suppliers.Abstractions;

public interface ISupplierReadStore
{
    Task<IReadOnlyList<SupplierReadModel>> GetAllAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<SupplierSearchReadModel> SearchAsync(
        string? searchQuery,
        decimal? minCreditLimit,
        decimal? maxCreditLimit,
        int pageNumber,
        int pageSize,
        string sortBy,
        string sortOrder,
        long businessId,
        CancellationToken cancellationToken);

    Task<SupplierReadModel?> GetByIdAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken);

    Task<SupplierPerformanceDataReadModel?> GetPerformanceDataAsync(
        long supplierId,
        long businessId,
        CancellationToken cancellationToken);
}

public interface ISupplierReadScopeProvider
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
