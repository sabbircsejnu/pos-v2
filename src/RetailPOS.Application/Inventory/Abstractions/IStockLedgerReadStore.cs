using RetailPOS.Application.Inventory.StockLedger;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockLedgerReadStore
{
    Task<StockLedgerReadModel?> GetByIdAsync(
        long id,
        StockLedgerReadScope scope,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockLedgerReadModel>> GetByVariantAndLocationAsync(
        long variantId,
        long locationId,
        string locationType,
        StockLedgerReadScope scope,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockLedgerReadModel>> GetByReferenceAsync(
        string referenceType,
        long referenceId,
        StockLedgerReadScope scope,
        CancellationToken cancellationToken);

    Task<StockLedgerSearchReadModel> SearchAsync(
        StockLedgerSearchFilter filter,
        StockLedgerReadScope scope,
        CancellationToken cancellationToken);
}

public interface IStockLedgerReadScopeProvider
{
    Task<StockLedgerReadScope> GetAsync(CancellationToken cancellationToken);
}
