namespace RetailPOS.Application.Products.Abstractions;

public interface IProductVariationCommandStore
{
    Task AssignAsync(
        long productId,
        IReadOnlyList<long> variationIds,
        IReadOnlyDictionary<long, List<long>> selectedOptionsByVariation,
        CancellationToken cancellationToken);
}
