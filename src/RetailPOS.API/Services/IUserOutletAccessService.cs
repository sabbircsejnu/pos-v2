using RetailPOS.API.DTOs.Outlet;
using RetailPOS.Application.Identity.LocationAccess;

namespace RetailPOS.API.Services;

/// <summary>
/// Centralised resolution of which outlets/warehouses the *current* user is
/// allowed to query, plus their active/default outlet. Backed by
/// <see cref="IRoleSwitchContext"/> so it honours role-switch state.
/// </summary>
public interface IUserOutletAccessService
{
    /// <summary>The list of outlets/warehouses the current user can read from.</summary>
    Task<AuthorizedOutletsDto> GetAuthorizedOutletsAsync();

    /// <summary>
    /// Locations selectable under an explicit operation policy. Prefer this over the parameterless overload
    /// so each module states which access setting (Step 3 scope, Step 5 assignments, business-wide) applies.
    /// </summary>
    Task<AuthorizedOutletsDto> GetAuthorizedOutletsAsync(LocationAccessPolicy policy);

    /// <summary>Policy-aware read-side resolution of a requested location.</summary>
    Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeLocationAsync(
        LocationAccessPolicy policy,
        long? requestedLocationId,
        string? requestedLocationType);

    /// <summary>Policy-aware write-side enforcement of a location.</summary>
    Task<(long LocationId, string LocationType)> EnforceWriteLocationAsync(
        LocationAccessPolicy policy,
        long requestedLocationId,
        string requestedLocationType);

    /// <summary>
    /// Gets the current user's locations within an explicitly selected Business.
    /// Intended for endpoints that require Business-scoped access.
    /// </summary>
    Task<AuthorizedOutletsDto> GetAuthorizedOutletsForBusinessAsync(long businessId);

    /// <summary>
    /// Validates an inbound outlet/warehouse filter against the user's authorized set.
    /// <para>
    /// - When <paramref name="requestedLocationId"/> is null, resolves to the caller's default scope.
    ///   BusinessOwner can query all locations; users with multiple authorized outlets can query
    ///   all outlets; others are pinned to their default outlet.
    /// </para>
    /// - When supplied, throws <see cref="UnauthorizedAccessException"/> if the user is not
    ///   permitted to read from that location.
    /// </summary>
    Task<(long? LocationId, string? LocationType)> ResolveAndAuthorizeLocationAsync(
        long? requestedLocationId,
        string? requestedLocationType);

    /// <summary>
    /// Read-side outlet filter for list/report endpoints that only deal with outlets
    /// (not warehouses).
    /// - Non-BusinessOwner: returns the user's default outlet.
    /// - BusinessOwner: validates the supplied id (if any) against the authorized set;
    ///   null means "all outlets".
    /// </summary>
    Task<long?> ResolveAndAuthorizeOutletFilterAsync(long? requestedOutletId);

    /// <summary>
    /// Write-side enforcement for transaction endpoints that record an outletId
    /// (sales, POS, hold, void, refund, etc.).
    /// - Non-BusinessOwner: forces the user's default outlet. The supplied id is
    ///   ignored unless it equals the default — otherwise an UnauthorizedAccessException
    ///   is thrown so the request is rejected, not silently rewritten.
    /// - BusinessOwner: validates that the supplied outlet exists in the authorized set.
    /// Throws if the user has no default outlet.
    /// </summary>
    Task<long> EnforceWriteOutletAsync(long? requestedOutletId);

    /// <summary>
    /// Write-side enforcement for transaction endpoints whose location can be either
    /// an outlet or a warehouse (stock adjustments, stock transfers, etc.).
    /// - Non-BusinessOwner: only the user's default outlet is permitted. Any other
    ///   location (including any warehouse) is rejected.
    /// - BusinessOwner: validates the (id, type) against the authorized set.
    /// </summary>
    Task<(long LocationId, string LocationType)> EnforceWriteLocationAsync(
        long requestedLocationId,
        string requestedLocationType);
}
