using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IPriceRuleRepository
{
    Task<PriceRule?> GetByIdAsync(long id);

    Task<IEnumerable<PriceRule>> GetAllAsync(string? ruleType = null, bool? isActive = null);

    Task<(IEnumerable<PriceRule> Rules, int TotalCount)> SearchAsync(
        string? searchQuery,
        string? ruleType,
        bool? isActive,
        int pageNumber,
        int pageSize);

    /// <summary>
    /// Returns all active, date-valid rules that match this cart-line context.
    /// Ordered by Priority DESC, Id DESC (most recent wins on tie).
    /// Caller picks First() as the winning rule.
    /// </summary>
    Task<IEnumerable<PriceRule>> GetMatchingRulesAsync(
        long variantId,
        long productId,
        long categoryId,
        long? outletId,
        int quantity,
        DateTime asOfUtc);

    Task<PriceRule> CreateAsync(PriceRule rule);
    Task<PriceRule> UpdateAsync(PriceRule rule);
    Task<bool> DeleteAsync(long id);
    Task<bool> NameExistsAsync(string name, long? excludeId = null);
}
