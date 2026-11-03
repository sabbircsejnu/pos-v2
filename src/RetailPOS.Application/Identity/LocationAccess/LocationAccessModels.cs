namespace RetailPOS.Application.Identity.LocationAccess;

public sealed record UserOutletAccessContext(
    long? UserId,
    bool IsSuperAdmin,
    bool IsBusinessOwner,
    long? EffectiveBusinessId,
    long? EffectiveOutletId,
    bool HasWildcardPermission = false);

public sealed record AuthorizedLocationReadModel(long Id, string Name, string Type);

public sealed record AuthorizedOutletsReadModel(
    IReadOnlyList<AuthorizedLocationReadModel> Outlets,
    IReadOnlyList<AuthorizedLocationReadModel> Warehouses,
    IReadOnlyList<AuthorizedLocationReadModel> DestinationOutlets,
    IReadOnlyList<AuthorizedLocationReadModel> DestinationWarehouses,
    long? DefaultOutletId,
    bool IsBusinessOwner,
    bool IsGlobalAccess,
    string AccessScope,
    long? DefaultLocationId,
    string? DefaultLocationType,
    LocationAccessPolicy Policy = LocationAccessPolicy.InventoryStockView);

public sealed record UserLocationAccessUser(
    long? BusinessId,
    long? OutletId,
    string? InventoryLocationAccessScope);

public sealed record UserOutletAssignmentReadModel(
    long AssignmentId,
    long OutletId,
    bool IsPrimary,
    string? OutletName,
    long? OutletBusinessId);

public sealed record UserWarehouseAssignmentReadModel(
    long AssignmentId,
    long WarehouseId,
    bool IsPrimary,
    string? WarehouseName,
    long? WarehouseBusinessId);
