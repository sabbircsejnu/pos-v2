using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Application.Pricing.Abstractions;

public interface IPriceRuleReadStore
{
    Task<PriceRuleSearchDataReadModel> SearchAsync(
        string? searchQuery,
        string? ruleType,
        bool? isActive,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PriceRuleReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken);
}
