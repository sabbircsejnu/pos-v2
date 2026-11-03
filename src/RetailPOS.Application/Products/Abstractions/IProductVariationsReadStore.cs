using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IProductVariationsReadStore
{
    Task<ProductVariationsReadModel?> FindByProductIdAsync(
        long productId,
        CancellationToken cancellationToken);
}
