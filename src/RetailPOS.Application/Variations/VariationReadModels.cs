namespace RetailPOS.Application.Variations;

public sealed record VariationReadModel(
    long Id,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool AutoSelectAllOptions,
    IReadOnlyList<VariationOptionReadModel> Options);

public sealed record VariationOptionReadModel(
    long Id,
    long VariationId,
    string VariationName,
    string Name,
    decimal PriceAdjustment,
    int DisplayOrder,
    bool IsActive);

public sealed record VariationProductReference(long Id, string Name);
