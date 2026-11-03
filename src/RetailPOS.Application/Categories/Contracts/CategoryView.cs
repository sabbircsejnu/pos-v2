namespace RetailPOS.Application.Categories.Contracts;

public sealed record CategoryView(
    long Id,
    string Name,
    string? Description,
    long? ParentCategoryId,
    string? ParentCategoryName,
    string? ImageUrl,
    int DisplayOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Level,
    int ChildCount,
    int ProductCount);
