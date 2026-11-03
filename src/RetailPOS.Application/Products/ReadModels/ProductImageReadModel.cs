namespace RetailPOS.Application.Products.ReadModels;

public sealed record ProductImageReadModel(
    long Id,
    long ProductId,
    string OriginalName,
    string MimeType,
    int Size,
    int Width,
    int Height,
    string ThumbUrl,
    string MediumUrl,
    string OriginalUrl,
    bool IsPrimary,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt);
