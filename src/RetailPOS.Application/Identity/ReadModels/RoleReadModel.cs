namespace RetailPOS.Application.Identity.ReadModels;

public sealed record RoleReadModel(
    long Id,
    string Name,
    IReadOnlyList<string> Permissions,
    int UserCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);
