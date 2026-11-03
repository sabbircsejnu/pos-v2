namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

public interface IStockCountUploadStore
{
    Task<long> UploadAsync(
        StockCountUploadInput input,
        CancellationToken cancellationToken);
}
