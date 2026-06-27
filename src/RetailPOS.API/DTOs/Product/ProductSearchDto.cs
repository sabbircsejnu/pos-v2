namespace RetailPOS.API.DTOs.Product;

/// <summary>
/// Search/filter parameters for products
/// </summary>
public class ProductSearchDto
{
    public string? SearchQuery { get; set; }
    public long? CategoryId { get; set; }
    /// <summary>Filter by status. Accepted values: "active", "inactive", "draft". Null returns all products.</summary>
    public string? Status { get; set; }
    public bool? HasVariants { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "Name";
    public string SortOrder { get; set; } = "asc";
}
