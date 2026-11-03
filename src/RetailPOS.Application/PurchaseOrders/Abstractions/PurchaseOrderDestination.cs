using RetailPOS.Core.Entities;

namespace RetailPOS.Application.PurchaseOrders.Abstractions;

/// <summary>Destination location of a purchase order (an Outlet or a Warehouse).</summary>
public sealed record PurchaseOrderDestination(string LocationType, long LocationId)
{
    /// <summary>
    /// Resolves the destination from the new <c>locationType</c>/<c>locationId</c> pair, falling back
    /// to the legacy <c>warehouseId</c> field so existing Warehouse-based clients keep working.
    /// </summary>
    public static PurchaseOrderDestination Resolve(
        string? locationType,
        long? locationId,
        long? legacyWarehouseId)
    {
        if (locationId is > 0)
        {
            var type = string.IsNullOrWhiteSpace(locationType)
                ? PurchaseLocationTypes.Warehouse
                : PurchaseLocationTypes.Normalize(locationType);
            return new PurchaseOrderDestination(type, locationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(locationType)
            && PurchaseLocationTypes.Normalize(locationType) == PurchaseLocationTypes.Outlet)
            throw new InvalidOperationException("A destination outlet is required.");

        if (legacyWarehouseId is > 0)
            return new PurchaseOrderDestination(PurchaseLocationTypes.Warehouse, legacyWarehouseId.Value);

        throw new InvalidOperationException(
            "A destination location (outlet or warehouse) is required.");
    }
}

/// <summary>Business ownership and display name of a purchase destination.</summary>
public sealed record PurchaseOrderLocationScope(long? BusinessId, string Name);

public static class PurchaseOrderLocationText
{
    public static string Label(string locationType) =>
        locationType == PurchaseLocationTypes.Outlet ? "Outlet" : "Warehouse";
}

/// <summary>
/// Verifies that the current user may record purchases/receipts for a destination location.
/// Throws <see cref="UnauthorizedAccessException"/> when access is denied.
/// </summary>
public interface IPurchaseOrderLocationAuthorizer
{
    Task AuthorizeAsync(
        string locationType,
        long locationId,
        CancellationToken cancellationToken);
}
