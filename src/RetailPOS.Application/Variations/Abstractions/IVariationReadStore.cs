namespace RetailPOS.Application.Variations.Abstractions;

public interface IVariationReadStore
{
    Task<IReadOnlyList<VariationReadModel>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<VariationReadModel?> GetByIdAsync(
        long variationId,
        CancellationToken cancellationToken);

    Task<VariationOptionReadModel?> GetOptionByIdAsync(
        long optionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VariationProductReference>> GetProductsUsingVariationAsync(
        long variationId,
        CancellationToken cancellationToken);
}
