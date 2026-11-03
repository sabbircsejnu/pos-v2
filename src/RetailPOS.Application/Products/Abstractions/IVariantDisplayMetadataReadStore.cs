using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IVariantDisplayMetadataReadStore
{
    Task<IReadOnlyList<VariantDisplayVariationReadModel>> GetAsync(
        CancellationToken cancellationToken);
}
