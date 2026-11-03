using RetailPOS.Application.Products.Commands;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Products.Abstractions;

public interface IProductCreateStore
{
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken);

    Task<bool> BarcodeExistsAsync(string barcode, CancellationToken cancellationToken);

    Task<bool> ProductCodeExistsAsync(string productCode, CancellationToken cancellationToken);

    Task<long> CreateAsync(
        Product product,
        IReadOnlyList<ProductVariantCreation> variants,
        CancellationToken cancellationToken);
}
