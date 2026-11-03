using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IMobileProductCatalogueReadStore
{
    Task<MobileProductCategoryPageReadModel> ListCategoriesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<MobileProductCataloguePageReadModel> SearchAsync(
        MobileProductCatalogueCriteria criteria,
        CancellationToken cancellationToken);

    Task<MobileProductReadModel?> FindProductAsync(
        long productId,
        CancellationToken cancellationToken);

    Task<MobileProductVariantPageReadModel?> FindVariantsAsync(
        long productId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<MobileProductVariantReadModel?> FindVariantAsync(
        long productId,
        long variantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MobileProductBarcodeMatchReadModel>> FindBarcodeMatchesAsync(
        string barcode,
        CancellationToken cancellationToken);
}
