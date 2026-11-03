namespace RetailPOS.Application.Products.Abstractions;

public interface IProductSkuStore
{
    Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken);
}
