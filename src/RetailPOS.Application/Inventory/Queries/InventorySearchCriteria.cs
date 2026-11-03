namespace RetailPOS.Application.Inventory.Queries;

public sealed record InventorySearchCriteria(
    string? ProductSearch,
    long? VariantId,
    string? VariantSearch,
    IReadOnlyList<VariantAttributeFilterCriteria>? VariantAttributeFilters,
    long? CategoryId,
    InventoryLocationScope LocationScope,
    bool? LowStockOnly,
    bool? OutOfStockOnly,
    bool? ExpiringSoon,
    int? ExpiringWithinDays,
    int Page,
    int PageSize);

public sealed record VariantAttributeFilterCriteria(
    string AttributeName,
    IReadOnlyList<string>? Values);
