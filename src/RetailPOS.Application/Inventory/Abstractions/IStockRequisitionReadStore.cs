using RetailPOS.Application.Inventory.StockRequisitions;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockRequisitionReadStore
{
    Task<StockRequisitionReadModel?> GetByIdAsync(
        long id,
        long businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockRequisitionReadModel>> GetAllAsync(
        StockRequisitionFilter filter,
        long businessId,
        CancellationToken cancellationToken);

    Task<StockRequisitionSearchResult> SearchAsync(
        StockRequisitionFilter filter,
        long businessId,
        CancellationToken cancellationToken);
}

public interface IStockRequisitionBusinessScopeProvider
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
