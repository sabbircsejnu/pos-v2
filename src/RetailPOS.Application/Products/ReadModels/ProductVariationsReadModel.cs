namespace RetailPOS.Application.Products.ReadModels;

public sealed record ProductVariationsReadModel(
    IReadOnlyList<ProductVariationReadModel> Variations);

public sealed record ProductVariationReadModel(
    long VariationId,
    string VariationName,
    bool IsRequired,
    IReadOnlyList<VariationOptionReadModel> Options,
    IReadOnlyList<long> SelectedOptionIds);

public sealed record VariationOptionReadModel(
    long Id,
    string Name,
    decimal PriceAdjustment);
