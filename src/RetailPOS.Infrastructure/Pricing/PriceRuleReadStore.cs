using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pricing;

public sealed class PriceRuleReadStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : IPriceRuleReadStore
{
    public async Task<PriceRuleSearchDataReadModel> SearchAsync(
        string? searchQuery,
        string? ruleType,
        bool? isActive,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var query = dbContext.PriceRules.AsNoTracking().Where(rule => rule.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            query = query.Where(rule =>
                EF.Functions.ILike(rule.Name, $"%{searchQuery}%") ||
                (rule.Description != null &&
                 EF.Functions.ILike(rule.Description, $"%{searchQuery}%")));
        }

        if (!string.IsNullOrWhiteSpace(ruleType))
        {
            query = query.Where(rule => rule.RuleType == ruleType.ToLower());
        }

        if (isActive.HasValue)
        {
            query = query.Where(rule => rule.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rules = await query
            .OrderByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(rule => ToReadModel(rule))
            .ToListAsync(cancellationToken);

        return new PriceRuleSearchDataReadModel(rules, totalCount);
    }

    public Task<PriceRuleReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.PriceRules
            .AsNoTracking()
            .Where(rule => rule.Id == id && rule.BusinessId == businessId)
            .Select(rule => ToReadModel(rule))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static PriceRuleReadModel ToReadModel(Core.Entities.PriceRule rule) =>
        new(
            rule.Id,
            rule.Name,
            rule.Description,
            rule.RuleType,
            rule.TargetId,
            rule.DiscountType,
            rule.DiscountValue,
            rule.MinQuantity,
            rule.Priority,
            rule.ValidFrom,
            rule.ValidTo,
            rule.IsActive,
            rule.CreatedAt);
}
