namespace RetailPOS.Application.Identity.ReadModels;

public sealed record UserListReadModel(
    IReadOnlyList<UserReadModel> Users,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record UserReadModel(
    long Id,
    string Name,
    string Email,
    long? RoleId,
    string? RoleName,
    long? OutletId,
    string? OutletName,
    long? BusinessId,
    string? BusinessName,
    string InventoryLocationAccessScope,
    string? DefaultLocationType,
    long? DefaultLocationId,
    IReadOnlyList<string> UserPermissions,
    IReadOnlyList<UserOutletAssignmentReadModel> OutletAssignments,
    IReadOnlyList<UserWarehouseAssignmentReadModel> WarehouseAssignments,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record UserOutletAssignmentReadModel(
    long Id,
    long OutletId,
    string? OutletName,
    bool IsPrimary,
    bool IsActive);

public sealed record UserWarehouseAssignmentReadModel(
    long Id,
    long WarehouseId,
    string? WarehouseName,
    bool IsPrimary,
    bool IsActive);
