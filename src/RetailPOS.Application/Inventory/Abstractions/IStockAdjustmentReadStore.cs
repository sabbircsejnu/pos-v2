using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockAdjustmentReadStore
{
    Task<StockAdjustmentReadModel?> GetByIdAsync(
        long id,
        StockAdjustmentReadScope scope,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockAdjustmentReadModel>> GetAllAsync(
        StockAdjustmentListFilter filter,
        StockAdjustmentReadScope scope,
        CancellationToken cancellationToken);

    Task<StockAdjustmentSearchReadModel> SearchAsync(
        StockAdjustmentSearchFilter filter,
        StockAdjustmentReadScope scope,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockAdjustmentReadModel>> GetHistoryAsync(
        long variantId,
        long locationId,
        StockAdjustmentReadScope scope,
        CancellationToken cancellationToken);
}

public interface IStockAdjustmentReadScopeProvider
{
    Task<StockAdjustmentReadScope> GetAsync(CancellationToken cancellationToken);
}
