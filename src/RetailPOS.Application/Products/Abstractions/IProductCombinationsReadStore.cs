using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IProductCombinationsReadStore
{
    Task<IReadOnlyList<ProductCombinationModel>?> GetByProductIdAsync(
        long productId,
        CancellationToken cancellationToken);
}
