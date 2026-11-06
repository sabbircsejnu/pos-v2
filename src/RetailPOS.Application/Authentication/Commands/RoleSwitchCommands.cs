using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Context;
using RetailPOS.Application.Authentication.Contracts;

namespace RetailPOS.Application.Authentication.Commands;

public sealed record SwitchRoleCommand(
    long RealUserId,
    string? ActingRole,
    long? ActingOutletId,
    string? ActingLocationType = null,
    long? ActingLocationId = null) : ICommand<AuthenticationResult>;

public sealed class SwitchRoleCommandHandler(
    IAuthenticationStore store,
    IAuthenticationCredentialsIssuer credentialsIssuer,
    UserContextPolicy contextPolicy)
    : IRequestHandler<SwitchRoleCommand, AuthenticationResult>
{
    public async Task<AuthenticationResult> Handle(
        SwitchRoleCommand request,
        CancellationToken cancellationToken)
    {
        var user = await store.FindActiveUserForRoleSwitchAsync(request.RealUserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("User not found or inactive.");
        }

        if (string.IsNullOrWhiteSpace(request.ActingRole))
        {
            throw new InvalidOperationException("ActingRole is required.");
        }

        var (locationType, locationId) = ResolveRequestedLocation(request);
        var resolved = await contextPolicy.ResolveAsync(
            user,
            request.ActingRole,
            locationType,
            locationId,
            cancellationToken);
        var credentials = credentialsIssuer.IssueForRoleSwitch(
            ToAuthenticationUser(user),
            resolved.Permissions,
            resolved.ToClaims());
        await store.PersistRefreshTokenAsync(
            user.Id,
            credentials.RefreshToken,
            credentials.RefreshTokenExpiresAt,
            cancellationToken);

        return new AuthenticationResult(
            credentials.AccessToken,
            credentials.RefreshToken,
            credentials.AccessTokenExpiresAt,
            new AuthenticationUserProfile(
                user.Id,
                user.BusinessId,
                user.Name,
                user.Email,
                resolved.Role.Name,
                resolved.Permissions,
                resolved.OutletId,
                resolved.OutletName,
                user.RoleName,
                IsBusinessOwner: UserContextPolicy.IsOwner(user),
                MustResetPassword: user.MustResetPassword,
                ActingRoleName: resolved.Role.Name,
                ActingOutletId: resolved.OutletId,
                ActingOutletName: resolved.OutletName,
                IsRoleSwitched: true,
                ActingLocationType: resolved.LocationType,
                ActingWarehouseId: resolved.WarehouseId,
                ActingWarehouseName: resolved.WarehouseName));
    }

    // ActingOutletId is the original outlet-only contract and stays valid on its own.
    private static (string? Type, long? Id) ResolveRequestedLocation(SwitchRoleCommand request)
    {
        if (request.ActingOutletId is not long outletId)
        {
            return (request.ActingLocationType, request.ActingLocationId);
        }

        var typeConflicts = !string.IsNullOrWhiteSpace(request.ActingLocationType)
            && !string.Equals(
                request.ActingLocationType.Trim(),
                UserContextLocationTypes.Outlet,
                StringComparison.OrdinalIgnoreCase);
        if (typeConflicts || (request.ActingLocationId.HasValue && request.ActingLocationId != outletId))
        {
            throw new InvalidOperationException("ActingOutletId conflicts with the selected location.");
        }

        return (UserContextLocationTypes.Outlet, outletId);
    }

    internal static AuthenticationUser ToAuthenticationUser(AuthenticationRoleSwitchUser user) =>
        new(
            user.Id,
            user.BusinessId,
            user.Name,
            user.Email,
            passwordHash: string.Empty,
            user.RoleId,
            user.RoleName,
            user.RolePermissions,
            user.OutletId,
            user.OutletName,
            user.PermissionOverrides,
            user.IsActive,
            user.MustResetPassword);
}

/// <param name="IsCurrentlySwitched">
/// Lets a non-owner leave a switched session even after their switching access was removed.
/// </param>
public sealed record ReturnOwnerCommand(long RealUserId, bool IsCurrentlySwitched = false)
    : ICommand<AuthenticationResult>;

public sealed class ReturnOwnerCommandHandler(
    IAuthenticationStore store,
    IAuthenticationCredentialsIssuer credentialsIssuer)
    : IRequestHandler<ReturnOwnerCommand, AuthenticationResult>
{
    public async Task<AuthenticationResult> Handle(
        ReturnOwnerCommand request,
        CancellationToken cancellationToken)
    {
        var user = await store.FindActiveUserForRoleSwitchAsync(request.RealUserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("User not found or inactive.");
        }

        var isOwner = UserContextPolicy.IsOwner(user);
        if (!isOwner && !request.IsCurrentlySwitched)
        {
            throw new UnauthorizedAccessException("There is no switched context to return from.");
        }

        var permissions = LoginCommandHandler.BuildEffectivePermissions(
            user.RolePermissions,
            user.PermissionOverrides);
        var credentials = credentialsIssuer.Issue(
            SwitchRoleCommandHandler.ToAuthenticationUser(user),
            permissions);
        await store.PersistRefreshTokenAsync(
            user.Id,
            credentials.RefreshToken,
            credentials.RefreshTokenExpiresAt,
            cancellationToken);

        return new AuthenticationResult(
            credentials.AccessToken,
            credentials.RefreshToken,
            credentials.AccessTokenExpiresAt,
            new AuthenticationUserProfile(
                user.Id,
                user.BusinessId,
                user.Name,
                user.Email,
                user.RoleName,
                permissions,
                user.OutletId,
                user.OutletName,
                user.RoleName,
                IsBusinessOwner: isOwner,
                MustResetPassword: user.MustResetPassword));
    }
}
