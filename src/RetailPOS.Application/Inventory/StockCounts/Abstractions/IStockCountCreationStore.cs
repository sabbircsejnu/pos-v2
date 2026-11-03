namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

public interface IStockCountCreationStore
{
    Task<long> CreateAsync(
        StockCountCreateInput input,
        CancellationToken cancellationToken);
}
