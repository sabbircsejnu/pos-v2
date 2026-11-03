namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

public interface IStockCountSearchReadStore
{
    Task<StockCountSearchResultReadModel> SearchAsync(
        StockCountReadScope scope,
        StockCountSearchFilter filter,
        CancellationToken cancellationToken);

    Task<StockCountDetailReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken);
}

public interface IStockCountSearchReadScopeProvider
{
    Task<StockCountReadScope> GetCreateScopeAsync(
        long? locationId,
        string? locationType,
        CancellationToken cancellationToken);

    Task<StockCountReadScope> GetAsync(
        long? locationId,
        string? locationType,
        CancellationToken cancellationToken);

    Task EnsureDetailAccessAsync(
        long businessId,
        long locationId,
        string locationType,
        CancellationToken cancellationToken);
}
