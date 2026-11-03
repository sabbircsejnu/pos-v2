namespace RetailPOS.Application.Products.ReadModels;

public sealed record ProductSearchCriteria(
    string? SearchQuery,
    long? CategoryId,
    string? Status,
    bool? HasVariants,
    decimal? MinPrice,
    decimal? MaxPrice,
    int PageNumber,
    int PageSize,
    string SortBy,
    string SortOrder)
{
    public long? OutletId { get; init; }
    public long? WarehouseId { get; init; }
}

public sealed record ProductSearchReadModel(
    IReadOnlyList<ProductReadModel> Products,
    int TotalCount,
    int PageNumber,
    int PageSize);
