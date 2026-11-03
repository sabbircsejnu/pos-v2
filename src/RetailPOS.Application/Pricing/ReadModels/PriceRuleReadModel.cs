namespace RetailPOS.Application.Pricing.ReadModels;

public sealed record PriceRuleReadModel(
    long Id,
    string Name,
    string? Description,
    string RuleType,
    long? TargetId,
    string DiscountType,
    decimal DiscountValue,
    int MinQuantity,
    int Priority,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    bool IsActive,
    DateTime CreatedAt);

public sealed record PriceRuleSearchDataReadModel(
    IReadOnlyList<PriceRuleReadModel> Rules,
    int TotalCount);

public sealed record PriceRuleSearchReadModel(
    IReadOnlyList<PriceRuleReadModel> Rules,
    int TotalCount,
    int PageNumber,
    int PageSize);
