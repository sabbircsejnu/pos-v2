namespace RetailPOS.API.DTOs.Product;

public class ProductImageDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string OriginalName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public int Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string ThumbUrl { get; set; } = string.Empty;
    public string MediumUrl { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
