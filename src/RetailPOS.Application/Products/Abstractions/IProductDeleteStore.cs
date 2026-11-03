namespace RetailPOS.Application.Products.Abstractions;

public interface IProductDeleteStore
{
    Task DeleteAsync(long productId, CancellationToken cancellationToken);
}
