using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Authentication.Context;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Identity.Commands;

/// <summary>
/// Who may change a user's context switching. Lives in the use case because users.edit alone
/// must not be enough to widen anyone's acting roles or locations.
/// </summary>
internal static class UserContextAccessRules
{
    private static readonly string[] GuardedPermissions =
    [
        PermissionCatalog.UserContextAccessManage,
        PermissionCatalog.UserContextAccessGrantAll
    ];

    public static async Task<UserGovernanceProfile> AuthorizeAsync(
        IUserGovernanceStore store,
        IUserGovernanceContext context,
        UserGovernanceActor actor,
        UserGovernanceProfile profile,
        long? targetUserId,
        string targetRoleName,
        long? businessId,
        CancellationToken cancellationToken)
    {
        await EnsureCanGrantGuardedPermissionsAsync(
            store,
            context,
            actor,
            profile,
            targetUserId,
            cancellationToken);

        if (profile.ContextAccess is null)
        {
            return profile;
        }

        var requested = Normalize(profile.ContextAccess);
        var normalizedProfile = profile with { ContextAccess = requested };
        var current = targetUserId.HasValue
            ? await store.FindContextAccessAsync(targetUserId.Value, cancellationToken)
            : null;
        if (IsUnchanged(requested, current))
        {
            return normalizedProfile;
        }

        if (!context.HasPermission(PermissionCatalog.UserContextAccessManage))
        {
            throw new UnauthorizedAccessException("You are not allowed to change context switching access.");
        }

        if (actor.Id == targetUserId)
        {
            throw new UnauthorizedAccessException("You cannot change your own context switching access.");
        }

        if (requested.Enabled && IsExcludedTargetRole(targetRoleName))
        {
            throw new InvalidOperationException(
                $"Context switching cannot be configured for the {targetRoleName} role.");
        }

        if ((requested.LocationScope == User.ContextLocationAll || requested.RoleScope == User.ContextRoleAll)
            && !context.HasPermission(PermissionCatalog.UserContextAccessGrantAll))
        {
            throw new UnauthorizedAccessException("You are not allowed to grant All Roles or All Locations.");
        }

        await EnsureRolesCanBeMappedAsync(store, context, requested, businessId, cancellationToken);
        return normalizedProfile;
    }

    private static async Task EnsureRolesCanBeMappedAsync(
        IUserGovernanceStore store,
        IUserGovernanceContext context,
        UserContextAccessInput requested,
        long? businessId,
        CancellationToken cancellationToken)
    {
        if (requested.RoleIds is not { Count: > 0 } roleIds)
        {
            return;
        }

        if (businessId is not long scopedBusinessId)
        {
            throw new InvalidOperationException("Context roles can only be mapped for a user in a business.");
        }

        var eligible = (await store.ListBusinessContextRolesAsync(
                scopedBusinessId,
                UserContextRoleRules.DefaultBusinessRoleNames,
                cancellationToken))
            .Where(role => UserContextRoleRules.IsEligibleActingRole(role.Name, role.Permissions))
            .ToDictionary(role => role.Id);

        foreach (var roleId in roleIds)
        {
            if (!eligible.TryGetValue(roleId, out var role))
            {
                throw new UnauthorizedAccessException($"Role #{roleId} cannot be mapped for context switching.");
            }

            // Nobody hands out more through a mapping than they hold themselves.
            if (LoginCommandHandler.ParsePermissionJson(role.Permissions)
                .Any(permission => !context.HasPermission(permission)))
            {
                throw new UnauthorizedAccessException(
                    $"Role '{role.Name}' has permissions you do not hold, so you cannot map it.");
            }
        }
    }

    // Per-user overrides could otherwise hand these keys out with users.edit alone.
    private static async Task EnsureCanGrantGuardedPermissionsAsync(
        IUserGovernanceStore store,
        IUserGovernanceContext context,
        UserGovernanceActor actor,
        UserGovernanceProfile profile,
        long? targetUserId,
        CancellationToken cancellationToken)
    {
        var requested = GuardedPermissions
            .Where(key => profile.UserPermissions?.Contains(key, StringComparer.OrdinalIgnoreCase) == true)
            .ToArray();
        if (requested.Length == 0)
        {
            return;
        }

        var existing = targetUserId.HasValue
            ? await store.FindPermissionOverridesAsync(targetUserId.Value, cancellationToken)
            : [];
        foreach (var key in requested.Where(key => !existing.Contains(key, StringComparer.OrdinalIgnoreCase)))
        {
            if (actor.Id == targetUserId || !context.HasPermission(key))
            {
                throw new UnauthorizedAccessException($"You are not allowed to grant the '{key}' permission.");
            }
        }
    }

    private static UserContextAccessInput Normalize(UserContextAccessInput input)
    {
        var locationScope = string.IsNullOrWhiteSpace(input.LocationScope)
            ? User.ContextLocationSelected
            : input.LocationScope.Trim().ToLowerInvariant();
        var roleScope = string.IsNullOrWhiteSpace(input.RoleScope)
            ? User.ContextRoleMapped
            : input.RoleScope.Trim().ToLowerInvariant();
        if (locationScope is not (User.ContextLocationSelected or User.ContextLocationAll))
        {
            throw new InvalidOperationException($"Context location scope '{input.LocationScope}' is not supported.");
        }

        if (roleScope is not (User.ContextRoleMapped or User.ContextRoleAll))
        {
            throw new InvalidOperationException($"Context role scope '{input.RoleScope}' is not supported.");
        }

        return new UserContextAccessInput(
            input.Enabled,
            locationScope,
            roleScope,
            (input.RoleIds ?? []).Where(id => id > 0).Distinct().Order().ToArray());
    }

    private static bool IsUnchanged(
        UserContextAccessInput requested,
        ReadModels.UserContextAccessReadModel? current) =>
        requested.Enabled == (current?.Enabled ?? false)
        && requested.LocationScope == (current?.LocationScope ?? User.ContextLocationSelected)
        && requested.RoleScope == (current?.RoleScope ?? User.ContextRoleMapped)
        && requested.RoleIds!.SequenceEqual((current?.RoleIds ?? []).Order());

    private static bool IsExcludedTargetRole(string roleName) =>
        string.Equals(roleName, SystemRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(roleName, SystemRoleNames.BusinessOwner, StringComparison.OrdinalIgnoreCase);
}
