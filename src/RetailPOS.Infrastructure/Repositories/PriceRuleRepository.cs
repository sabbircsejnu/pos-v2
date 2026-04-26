using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class PriceRuleRepository : IPriceRuleRepository
{
    private readonly RetailPOSDbContext _context;

    public PriceRuleRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<PriceRule?> GetByIdAsync(long id)
        => await _context.PriceRules.FirstOrDefaultAsync(r => r.Id == id);

    public async Task<IEnumerable<PriceRule>> GetAllAsync(string? ruleType = null, bool? isActive = null)
    {
        var query = _context.PriceRules.AsQueryable();

        if (!string.IsNullOrWhiteSpace(ruleType))
            query = query.Where(r => r.RuleType == ruleType.ToLower());

        if (isActive.HasValue)
            query = query.Where(r => r.IsActive == isActive.Value);

        return await query
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.Id)
            .ToListAsync();
    }

    public async Task<(IEnumerable<PriceRule> Rules, int TotalCount)> SearchAsync(
        string? searchQuery,
        string? ruleType,
        bool? isActive,
        int pageNumber,
        int pageSize)
    {
        var query = _context.PriceRules.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            // ILike is case-insensitive and uses PostgreSQL's index-friendly ILIKE operator.
            query = query.Where(r =>
                EF.Functions.ILike(r.Name, $"%{searchQuery}%") ||
                (r.Description != null && EF.Functions.ILike(r.Description, $"%{searchQuery}%")));
        }

        if (!string.IsNullOrWhiteSpace(ruleType))
            query = query.Where(r => r.RuleType == ruleType.ToLower());

        if (isActive.HasValue)
            query = query.Where(r => r.IsActive == isActive.Value);

        var totalCount = await query.CountAsync();

        var rules = await query
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (rules, totalCount);
    }

    public async Task<IEnumerable<PriceRule>> GetMatchingRulesAsync(
        long variantId,
        long productId,
        long categoryId,
        long? outletId,
        int quantity,
        DateTime asOfUtc)
    {
        // Fix: only include outlet-scoped rules when an outletId is actually provided.
        // Without this guard, `r.TargetId == outletId` when outletId is null translates to
        // `target_id IS NULL` in SQL and would incorrectly match any outlet rule with a null TargetId.
        var query = _context.PriceRules
            .Where(r =>
                r.IsActive &&
                r.MinQuantity <= quantity &&
                (r.ValidFrom == null || r.ValidFrom <= asOfUtc) &&
                (r.ValidTo   == null || r.ValidTo   >= asOfUtc) &&
                (
                    (r.RuleType == "variant"  && r.TargetId == variantId)  ||
                    (r.RuleType == "product"  && r.TargetId == productId)  ||
                    (r.RuleType == "category" && r.TargetId == categoryId) ||
                    (outletId.HasValue && r.RuleType == "outlet" && r.TargetId == outletId.Value) ||
                    r.RuleType == "global"
                ));

        // Deterministic order: highest Priority wins; on equal Priority, most recently created wins.
        // Take(1): only the single best match is ever used by the caller.
        return await query
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.Id)
            .Take(1)
            .ToListAsync();
    }

    public async Task<PriceRule> CreateAsync(PriceRule rule)
    {
        _context.PriceRules.Add(rule);
        await _context.SaveChangesAsync();
        return rule;
    }

    public async Task<PriceRule> UpdateAsync(PriceRule rule)
    {
        rule.UpdatedAt = DateTime.UtcNow;
        _context.PriceRules.Update(rule);
        await _context.SaveChangesAsync();
        return rule;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var rule = await _context.PriceRules.FindAsync(id);
        if (rule == null) return false;
        _context.PriceRules.Remove(rule);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> NameExistsAsync(string name, long? excludeId = null)
    {
        var lower = name.ToLower();
        return await _context.PriceRules
            .AnyAsync(r => r.Name.ToLower() == lower &&
                           (excludeId == null || r.Id != excludeId));
    }
}
