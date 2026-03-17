namespace RetailPOS.API.DTOs.Product;

/// <summary>
/// Paginated product list response
/// </summary>
public class ProductListDto
{
    public List<ProductDto> Products { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
