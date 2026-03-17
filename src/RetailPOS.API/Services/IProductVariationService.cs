using RetailPOS.API.DTOs.Product;

namespace RetailPOS.API.Services;

public interface IProductVariationService
{
    Task<List<ProductVariationDto>> GetProductVariationsAsync(long productId);
    Task AssignVariationsToProductAsync(long productId, List<long> variationIds);
    Task<List<CombinationDto>> GenerateAllCombinationsAsync(long productId);
    Task<List<CombinationDto>> GenerateSelectedCombinationsAsync(long productId, List<long> variationIds);
    Task<CombinationDto> CreateManualCombinationAsync(long productId, CreateCombinationRequest request);
    Task<List<CombinationDto>> GetProductCombinationsAsync(long productId);
    Task UpdateCombinationAsync(long variantId, UpdateCombinationRequest request);
    Task DeleteCombinationAsync(long variantId);
}
