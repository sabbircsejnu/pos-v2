using RetailPOS.Application.Pricing.ReadModels;

namespace RetailPOS.Application.Pricing.Abstractions;

public interface IPriceRuleCommandStore
{
    Task<bool> NameExistsAsync(
        string name,
        long? excludeId,
        CancellationToken cancellationToken);

    Task<bool> TargetExistsAsync(
        string ruleType,
        long targetId,
        CancellationToken cancellationToken);

    Task<PriceRuleReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken);

    Task<PriceRuleReadModel> CreateAsync(
        PriceRuleWriteData rule,
        CancellationToken cancellationToken);

    Task<PriceRuleReadModel?> UpdateAsync(
        long id,
        PriceRuleWriteData rule,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken);
}
