namespace RetailPOS.Application.Pricing.ReadModels;

public sealed record OutletPriceOverrideReadModel(
    long Id,
    long OutletId,
    string OutletName,
    long ProductVariantId,
    string VariantName,
    string ProductName,
    string OverrideType,
    decimal OverrideValue,
    bool IsActive,
    DateTime CreatedAt);

public sealed record OutletPriceOverrideListReadModel(
    IReadOnlyList<OutletPriceOverrideReadModel> Overrides,
    int TotalCount);
