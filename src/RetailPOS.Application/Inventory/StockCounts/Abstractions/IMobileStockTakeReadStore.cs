namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

/// <summary>
/// A null <c>ownerUserId</c> means the caller is not restricted to own sessions (StockCount.ViewAll, or a
/// write path that already holds its own permission); otherwise only sessions created or submitted by that
/// user are visible (StockCount.ViewOwn).
/// </summary>
public interface IMobileStockTakeReadStore
{
    Task<MobileStockTakePageReadModel> SearchAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? outletId,
        string? status,
        string? search,
        int page,
        int pageSize,
        long? ownerUserId,
        CancellationToken cancellationToken);

    Task<MobileStockTakeSummaryReadModel?> GetByIdAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId,
        long? ownerUserId,
        CancellationToken cancellationToken);

    Task<MobileStockTakeLinePageReadModel?> GetLinesAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId,
        int page,
        int pageSize,
        long? ownerUserId,
        CancellationToken cancellationToken);
}
