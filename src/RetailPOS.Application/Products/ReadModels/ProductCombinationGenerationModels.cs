namespace RetailPOS.Application.Products.ReadModels;

public sealed record ProductCombinationGenerationData(
    long ProductId,
    string? ProductCode,
    long CategoryId,
    decimal BasePrice,
    IReadOnlyList<long> AssignedVariationIds,
    IReadOnlyList<IReadOnlyList<ProductCombinationOption>> OptionGroups);

public sealed record ProductCombinationOption(
    long VariationId,
    long OptionId,
    string VariationName,
    string OptionName,
    int DisplayOrder,
    decimal PriceAdjustment);

public sealed record ManualProductCombinationData(
    long ProductId,
    string? ProductCode,
    long CategoryId,
    decimal BasePrice,
    IReadOnlyList<ProductCombinationOption> Options,
    bool CombinationAlreadyExists);

public sealed record ProductCombinationDraft(
    string Name,
    string? SizeCode,
    string? AttributeCode,
    decimal PriceAdjustment,
    IReadOnlyList<long> OptionIds,
    IReadOnlyList<ProductCombinationOption> Options,
    string AttributesJson);

public sealed record ProductCombinationModel(
    long Id,
    string Sku,
    string? Barcode,
    decimal PriceAdjustment,
    decimal CostAdjustment,
    IReadOnlyList<long> OptionIds,
    IReadOnlyList<ProductCombinationOption> Options,
    string CombinationName,
    decimal FinalPrice);
