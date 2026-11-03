using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IProductStockReadStore
{
    Task<IReadOnlyList<ProductReadModel>> EnrichAsync(
        IReadOnlyList<ProductReadModel> products,
        long? outletId,
        long? warehouseId,
        CancellationToken cancellationToken);
}
