namespace RetailPOS.Application.Inventory.ReadModels;

public sealed record VariantAttributeFilterReadModel(
    string AttributeName,
    IReadOnlyList<string> Values);
