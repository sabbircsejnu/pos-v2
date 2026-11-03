using RetailPOS.Application.Products.ReadModels;

namespace RetailPOS.Application.Products.Abstractions;

public interface IProductManualCombinationStore
{
    Task<ManualProductCombinationData?> GetCreationDataAsync(
        long productId,
        IReadOnlyList<long> optionIds,
        CancellationToken cancellationToken);

    Task<ProductCombinationModel> CreateAsync(
        ManualProductCombinationData data,
        IReadOnlyList<long> optionIds,
        string sku,
        string barcode,
        decimal priceAdjustment,
        decimal costAdjustment,
        CancellationToken cancellationToken);
}
