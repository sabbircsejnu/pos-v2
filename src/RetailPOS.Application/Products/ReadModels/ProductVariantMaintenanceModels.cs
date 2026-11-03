namespace RetailPOS.Application.Products.ReadModels;

public sealed record ProductVariantUpdateData(
    string? ProductCode,
    long CategoryId,
    IReadOnlyList<(string VariationName, string OptionName)> Options);

public sealed record ProductVariantDeleteData(
    bool HasSales,
    bool HasPurchaseOrderItems,
    bool HasStockTransferItems,
    bool HasStockAdjustmentLines);
