namespace RetailPOS.Application.Outlets.Abstractions;

public interface IOutletReadStore
{
    Task<IReadOnlyList<OutletReadModel>> GetAllAsync(
        long? businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OutletReadModel>> SearchAsync(
        string searchTerm,
        long? businessId,
        CancellationToken cancellationToken);

    Task<OutletReadModel?> GetByIdAsync(
        long outletId,
        long? businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OutletReadModel>> GetByBusinessIdAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<OutletStatsReadModel?> GetStatsAsync(
        long outletId,
        long? businessId,
        CancellationToken cancellationToken);
}

public interface IOutletScopeProvider
{
    Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken);
}
