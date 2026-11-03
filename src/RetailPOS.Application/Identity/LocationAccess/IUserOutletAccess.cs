namespace RetailPOS.Application.Identity.LocationAccess;

public interface IUserOutletAccess
{
    /// <summary>Legacy entry point; equivalent to <see cref="LocationAccessPolicy.InventoryStockView"/>.</summary>
    Task<AuthorizedOutletsReadModel> GetAuthorizedOutletsAsync(UserOutletAccessContext context);

    /// <summary>Locations selectable under the given operation policy (read/selection only, not a write grant).</summary>
    Task<AuthorizedOutletsReadModel> GetAuthorizedLocationsAsync(
        UserOutletAccessContext context,
        LocationAccessPolicy policy);

    Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeLocationAsync(
        UserOutletAccessContext context,
        LocationAccessPolicy policy,
        long? requestedLocationId,
        string? requestedLocationType);

    Task<(long LocationId, string LocationType)> EnforceWriteLocationAsync(
        UserOutletAccessContext context,
        LocationAccessPolicy policy,
        long requestedLocationId,
        string requestedLocationType);

    Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeLocationAsync(
        UserOutletAccessContext context,
        long? requestedLocationId,
        string? requestedLocationType);

    Task<long?> ResolveAndAuthorizeOutletFilterAsync(
        UserOutletAccessContext context,
        long? requestedOutletId);

    Task<long> EnforceWriteOutletAsync(UserOutletAccessContext context, long? requestedOutletId);

    Task<(long LocationId, string LocationType)> EnforceWriteLocationAsync(
        UserOutletAccessContext context,
        long requestedLocationId,
        string requestedLocationType);
}
