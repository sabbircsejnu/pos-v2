namespace RetailPOS.Application.Products.ReadModels;

public sealed record VariantDisplayVariationReadModel(
    string Name,
    int DisplayOrder,
    IReadOnlyList<string> ActiveOptionNames);
