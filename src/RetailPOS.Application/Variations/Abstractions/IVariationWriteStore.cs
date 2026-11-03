using RetailPOS.Application.Variations;

namespace RetailPOS.Application.Variations.Abstractions;

public interface IVariationWriteStore
{
    Task<bool> NameExistsAsync(
        string name,
        long? excludeVariationId,
        CancellationToken cancellationToken);

    Task<VariationReadModel> CreateAsync(
        VariationWriteModel variation,
        CancellationToken cancellationToken);

    Task<VariationReadModel?> UpdateAsync(
        long variationId,
        VariationWriteModel variation,
        CancellationToken cancellationToken);

    Task DeleteAsync(long variationId, CancellationToken cancellationToken);

    Task<VariationOptionReadModel> CreateOptionAsync(
        long variationId,
        VariationOptionWriteModel option,
        CancellationToken cancellationToken);

    Task<VariationOptionReadModel?> UpdateOptionAsync(
        long optionId,
        VariationOptionWriteModel option,
        CancellationToken cancellationToken);

    Task DeleteOptionAsync(long optionId, CancellationToken cancellationToken);
}
