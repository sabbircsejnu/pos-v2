namespace RetailPOS.Application.Variations;

public sealed record VariationWriteModel(
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool AutoSelectAllOptions,
    IReadOnlyList<VariationOptionWriteModel> Options);

public sealed record VariationOptionWriteModel(
    string Name,
    decimal PriceAdjustment,
    int DisplayOrder,
    bool IsActive);
