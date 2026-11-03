namespace RetailPOS.Application.Pricing.ReadModels;

public sealed record OutletPriceOverrideWriteData(
    long OutletId,
    long ProductVariantId,
    string OverrideType,
    decimal OverrideValue,
    bool IsActive);

public sealed record OutletPriceOverrideUpdateData(
    string OverrideType,
    decimal OverrideValue,
    bool IsActive);
