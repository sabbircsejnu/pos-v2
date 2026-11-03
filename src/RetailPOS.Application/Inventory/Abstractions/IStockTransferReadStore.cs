using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockTransferReadStore
{
    Task<StockTransferReadModel?> GetByIdAsync(
        long id,
        long businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockTransferReadModel>> GetAllAsync(
        StockTransferListFilter filter,
        StockTransferReadScope scope,
        CancellationToken cancellationToken);

    Task<StockTransferSearchResult> SearchAsync(
        StockTransferSearchFilter filter,
        StockTransferReadScope scope,
        CancellationToken cancellationToken);
}

public interface IStockTransferReadScopeProvider
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
