using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IProductCombinationGenerationStore
{
    Task<ProductCombinationGenerationData?> GetGenerationDataAsync(
        long productId,
        IReadOnlyList<long>? variationIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductCombinationModel>> CreateMissingCombinationsAsync(
        ProductCombinationGenerationData generationData,
        IReadOnlyList<ProductCombinationDraft> combinations,
        CancellationToken cancellationToken);
}
