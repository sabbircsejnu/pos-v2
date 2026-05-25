namespace RetailPOS.Core.Entities;

public class ProductImage
{
    public long Id { get; set; }
    public long ProductId { get; set; }

    public string OriginalName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public int Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public string OriginalPath { get; set; } = string.Empty;
    public string MediumPath { get; set; } = string.Empty;
    public string ThumbPath { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Product? Product { get; set; }
}
