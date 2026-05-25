using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories
{
    public interface IVariationRepository
    {
        Task<Variation?> GetByIdAsync(long id);
        Task<List<Variation>> GetAllAsync(bool includeInactive = false);
        Task<Variation> CreateAsync(Variation variation);
        Task<Variation> UpdateAsync(Variation variation);
        Task DeleteAsync(long id);
        Task<bool> NameExistsAsync(string name, long? excludeId = null);
        Task<List<Product>> GetProductsUsingVariationAsync(long variationId);
    }

    public interface IVariationOptionRepository
    {
        Task<VariationOption?> GetByIdAsync(long id);
        Task<List<VariationOption>> GetByVariationIdAsync(long variationId);
        Task<List<VariationOption>> GetByIdsAsync(List<long> ids);
        Task<VariationOption> CreateAsync(VariationOption option);
        Task<VariationOption> UpdateAsync(VariationOption option);
        Task DeleteAsync(long id);
    }
}
