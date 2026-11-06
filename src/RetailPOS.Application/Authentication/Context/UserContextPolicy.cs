using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Authentication.Context;

public sealed record UserContextOptions(
    bool CanSwitch,
    bool AllowAllLocations,
    IReadOnlyList<AuthenticationRole> Roles,
    IReadOnlyList<AuthorizedLocationReadModel> Outlets,
    IReadOnlyList<AuthorizedLocationReadModel> Warehouses)
{
    public static readonly UserContextOptions None = new(false, false, [], [], []);
}

public sealed record ResolvedUserContext(
    AuthenticationRole Role,
    IReadOnlyList<string> Permissions,
    string LocationType,
    AuthorizedLocationReadModel? Location)
{
    public long? OutletId => LocationType == UserContextLocationTypes.Outlet ? Location?.Id : null;
    public string? OutletName => LocationType == UserContextLocationTypes.Outlet ? Location?.Name : null;
    public long? WarehouseId => LocationType == UserContextLocationTypes.Warehouse ? Location?.Id : null;
    public string? WarehouseName => LocationType == UserContextLocationTypes.Warehouse ? Location?.Name : null;

    public AuthenticationActingRoleClaims ToClaims() =>
        new(Role.Id, Role.Name, Permissions, OutletId, OutletName, LocationType, WarehouseId, WarehouseName);
}

public static class UserContextLocationTypes
{
    public const string Outlet = "outlet";
    public const string Warehouse = "warehouse";
    public const string All = "all";
}

public static class UserContextRoleRules
{
    /// <summary>
    /// Roles provisioned for every business. Roles have no BusinessId, so any other role only
    /// counts as belonging to a business while a user of that business holds it.
    /// </summary>
    public static readonly IReadOnlyCollection<string> DefaultBusinessRoleNames =
    [
        "OutletManager", "SalesPerson", "AccountsAdmin", "WarehouseManager",
        "Cashier", "Salesman", "Stock Manager", "StockManager"
    ];

    /// <summary>Platform, owner and wildcard roles can never be taken on as an acting role.</summary>
    public static bool IsEligibleActingRole(string roleName, string? permissionsJson) =>
        !string.Equals(roleName, SystemRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(roleName, SystemRoleNames.BusinessOwner, StringComparison.OrdinalIgnoreCase)
        && !LoginCommandHandler.ParsePermissionJson(permissionsJson).Contains("*");
}

/// <summary>
/// The one definition of which roles and locations a user may switch to. The options endpoint, the
/// switch command and token refresh all use it, so a crafted request can never reach more than the
/// options offered to that user. Everything is read from persisted state, never from the client.
/// </summary>
public sealed class UserContextPolicy(IAuthenticationStore store, IUserOutletAccessStore locationStore)
{
    public async Task<UserContextOptions> GetOptionsAsync(
        AuthenticationRoleSwitchUser? user,
        CancellationToken cancellationToken)
    {
        if (user is null
            || !user.IsActive
            || user.BusinessId is not long businessId
            || string.Equals(user.RoleName, SystemRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return UserContextOptions.None;
        }

        var isOwner = IsOwner(user);
        if (!isOwner && !user.ContextSwitchEnabled)
        {
            return UserContextOptions.None;
        }

        var roles = await LoadRolesAsync(user, businessId, isOwner, cancellationToken);
        var allLocations = isOwner || user.ContextLocationScope == User.ContextLocationAll;
        var (outlets, warehouses) = allLocations
            ? (await locationStore.ListOutletsAsync(businessId), await locationStore.ListWarehousesAsync(businessId))
            : await LoadSelectedLocationsAsync(user, businessId);

        // An empty selection stays empty: it is never read as "all locations".
        var canSwitch = roles.Count > 0 && (allLocations || outlets.Count > 0 || warehouses.Count > 0);
        return canSwitch
            ? new UserContextOptions(true, allLocations, roles, outlets, warehouses)
            : UserContextOptions.None;
    }

    public async Task<ResolvedUserContext> ResolveAsync(
        AuthenticationRoleSwitchUser user,
        string? roleName,
        string? locationType,
        long? locationId,
        CancellationToken cancellationToken)
    {
        var options = await GetOptionsAsync(user, cancellationToken);
        if (!options.CanSwitch)
        {
            throw new UnauthorizedAccessException("Context switching is not enabled for your account.");
        }

        var role = options.Roles.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, roleName?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new UnauthorizedAccessException($"Role '{roleName}' is not available to you.");
        var (resolvedType, location) = ResolveLocation(options, NormalizeLocationType(locationType), locationId);

        // The user's own role keeps their personal overrides; any other role grants exactly that role.
        var permissions = !IsOwner(user) && role.Id == user.RoleId
            ? LoginCommandHandler.BuildEffectivePermissions(role.Permissions, user.PermissionOverrides)
            : LoginCommandHandler.ParsePermissionJson(role.Permissions);

        return new ResolvedUserContext(role, permissions, resolvedType, location);
    }

    /// <summary>Re-checks a context from an expiring token. Null means it is no longer authorized.</summary>
    public async Task<ResolvedUserContext?> RevalidateAsync(
        long userId,
        string? roleName,
        string? locationType,
        long? locationId,
        CancellationToken cancellationToken)
    {
        var user = await store.FindActiveUserForRoleSwitchAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        try
        {
            return await ResolveAsync(user, roleName, locationType, locationId, cancellationToken);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    public static bool IsOwner(AuthenticationRoleSwitchUser user) =>
        string.Equals(user.RoleName, SystemRoleNames.BusinessOwner, StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<AuthenticationRole>> LoadRolesAsync(
        AuthenticationRoleSwitchUser user,
        long businessId,
        bool isOwner,
        CancellationToken cancellationToken)
    {
        var candidates = isOwner || user.ContextRoleScope == User.ContextRoleAll
            ? await store.ListBusinessContextRolesAsync(
                businessId,
                UserContextRoleRules.DefaultBusinessRoleNames,
                cancellationToken)
            : await store.ListMappedContextRolesAsync(user.Id, businessId, cancellationToken);
        var eligible = candidates
            .Where(role => UserContextRoleRules.IsEligibleActingRole(role.Name, role.Permissions))
            .Where(role => role.Id != user.RoleId)
            .OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Staying on one's own role is not an escalation, so a non-owner can always change location only.
        if (!isOwner && user.RoleId is long ownRoleId && user.RoleName is not null)
        {
            eligible.Insert(0, new AuthenticationRole(ownRoleId, user.RoleName, user.RolePermissions ?? "[]"));
        }

        return eligible;
    }

    private async Task<(IReadOnlyList<AuthorizedLocationReadModel> Outlets,
        IReadOnlyList<AuthorizedLocationReadModel> Warehouses)> LoadSelectedLocationsAsync(
        AuthenticationRoleSwitchUser user,
        long businessId)
    {
        var outlets = (await locationStore.ListActiveOutletAssignmentsAsync(user.Id))
            .Where(assignment => assignment.OutletName is not null && assignment.OutletBusinessId == businessId)
            .Select(assignment => new AuthorizedLocationReadModel(
                assignment.OutletId,
                assignment.OutletName!,
                UserContextLocationTypes.Outlet))
            .ToList();
        var warehouses = (await locationStore.ListActiveWarehouseAssignmentsAsync(user.Id))
            .Where(assignment => assignment.WarehouseName is not null && assignment.WarehouseBusinessId == businessId)
            .Select(assignment => new AuthorizedLocationReadModel(
                assignment.WarehouseId,
                assignment.WarehouseName!,
                UserContextLocationTypes.Warehouse))
            .ToList();

        // The default outlet is already the user's own working location, with or without an assignment row.
        if (user.OutletId is long defaultOutletId && outlets.All(outlet => outlet.Id != defaultOutletId))
        {
            var defaultOutlet = (await locationStore.ListOutletsAsync(businessId))
                .FirstOrDefault(outlet => outlet.Id == defaultOutletId);
            if (defaultOutlet is not null)
            {
                outlets.Add(defaultOutlet);
            }
        }

        return (outlets, warehouses);
    }

    private static (string Type, AuthorizedLocationReadModel? Location) ResolveLocation(
        UserContextOptions options,
        string? type,
        long? locationId)
    {
        if (locationId is long id)
        {
            var candidates = type switch
            {
                UserContextLocationTypes.Outlet => options.Outlets,
                UserContextLocationTypes.Warehouse => options.Warehouses,
                UserContextLocationTypes.All =>
                    throw new InvalidOperationException("The All Locations context cannot name a location."),
                _ => throw new InvalidOperationException("A location type is required with a location.")
            };
            var location = candidates.FirstOrDefault(candidate => candidate.Id == id)
                ?? throw new UnauthorizedAccessException($"The selected {type} is not available to you.");
            return (type!, location);
        }

        if (type is UserContextLocationTypes.Outlet or UserContextLocationTypes.Warehouse)
        {
            throw new InvalidOperationException($"Select the {type} to switch to.");
        }

        if (options.AllowAllLocations)
        {
            return (UserContextLocationTypes.All, null);
        }

        if (type == UserContextLocationTypes.All)
        {
            throw new UnauthorizedAccessException("The All Locations context is not available to you.");
        }

        // Only a single valid context may be chosen on the user's behalf.
        return (options.Outlets.Count, options.Warehouses.Count) switch
        {
            (1, 0) => (UserContextLocationTypes.Outlet, options.Outlets[0]),
            (0, 1) => (UserContextLocationTypes.Warehouse, options.Warehouses[0]),
            _ => throw new InvalidOperationException("Select the location to switch to.")
        };
    }

    private static string? NormalizeLocationType(string? locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
        {
            return null;
        }

        var normalized = locationType.Trim().ToLowerInvariant();
        return normalized is UserContextLocationTypes.Outlet
            or UserContextLocationTypes.Warehouse
            or UserContextLocationTypes.All
            ? normalized
            : throw new InvalidOperationException($"Location type '{locationType}' is not supported.");
    }
}
