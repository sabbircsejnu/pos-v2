using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Pricing.Abstractions;
using RetailPOS.Application.Pricing.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pricing;

public sealed class PriceRuleCommandStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : IPriceRuleCommandStore
{
    public Task<bool> NameExistsAsync(
        string name,
        long? excludeId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var normalizedName = name.ToLower();
        return dbContext.PriceRules.AnyAsync(
            rule => rule.BusinessId == businessId &&
                    rule.Name.ToLower() == normalizedName &&
                    (!excludeId.HasValue || rule.Id != excludeId.Value),
            cancellationToken);
    }

    public Task<bool> TargetExistsAsync(
        string ruleType,
        long targetId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return ruleType switch
        {
            "variant" => dbContext.ProductVariants.AnyAsync(
                variant => variant.Id == targetId && variant.BusinessId == businessId,
                cancellationToken),
            "product" => dbContext.Products.AnyAsync(
                product => product.Id == targetId && product.BusinessId == businessId,
                cancellationToken),
            "category" => dbContext.Categories.AnyAsync(
                category => category.Id == targetId && category.BusinessId == businessId,
                cancellationToken),
            "outlet" => dbContext.Outlets.AnyAsync(
                outlet => outlet.Id == targetId && outlet.BusinessId == businessId,
                cancellationToken),
            _ => Task.FromResult(true)
        };
    }

    public Task<PriceRuleReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.PriceRules
            .Where(rule => rule.Id == id && rule.BusinessId == businessId)
            .Select(rule => ToReadModel(rule))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PriceRuleReadModel> CreateAsync(
        PriceRuleWriteData data,
        CancellationToken cancellationToken)
    {
        var rule = new PriceRule
        {
            BusinessId = businessScope.RequireBusinessId(),
            Name = data.Name,
            Description = data.Description,
            RuleType = data.RuleType,
            TargetId = data.TargetId,
            DiscountType = data.DiscountType,
            DiscountValue = data.DiscountValue,
            MinQuantity = data.MinQuantity,
            Priority = data.Priority,
            ValidFrom = data.ValidFrom,
            ValidTo = data.ValidTo,
            IsActive = data.IsActive
        };

        dbContext.PriceRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToReadModel(rule);
    }

    public async Task<PriceRuleReadModel?> UpdateAsync(
        long id,
        PriceRuleWriteData data,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var rule = await dbContext.PriceRules.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.BusinessId == businessId,
            cancellationToken);
        if (rule is null)
        {
            return null;
        }

        rule.Name = data.Name;
        rule.Description = data.Description;
        rule.RuleType = data.RuleType;
        rule.TargetId = data.TargetId;
        rule.DiscountType = data.DiscountType;
        rule.DiscountValue = data.DiscountValue;
        rule.MinQuantity = data.MinQuantity;
        rule.Priority = data.Priority;
        rule.ValidFrom = data.ValidFrom;
        rule.ValidTo = data.ValidTo;
        rule.IsActive = data.IsActive;
        rule.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToReadModel(rule);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var rule = await dbContext.PriceRules.FirstOrDefaultAsync(
            candidate => candidate.Id == id && candidate.BusinessId == businessId,
            cancellationToken);
        if (rule is null)
        {
            return false;
        }

        dbContext.PriceRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static PriceRuleReadModel ToReadModel(PriceRule rule) =>
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
