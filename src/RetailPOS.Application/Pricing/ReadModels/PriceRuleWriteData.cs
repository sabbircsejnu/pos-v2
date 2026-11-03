namespace RetailPOS.Application.Pricing.ReadModels;

public sealed record PriceRuleWriteData(
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
    bool IsActive);
