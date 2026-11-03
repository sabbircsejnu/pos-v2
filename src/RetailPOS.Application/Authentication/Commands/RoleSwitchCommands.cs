using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Contracts;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Authentication.Commands;

public sealed record SwitchRoleCommand(
    long RealUserId,
    string? ActingRole,
    long? ActingOutletId) : ICommand<AuthenticationResult>;

public sealed class SwitchRoleCommandHandler(
    IAuthenticationStore store,
    IAuthenticationCredentialsIssuer credentialsIssuer)
    : IRequestHandler<SwitchRoleCommand, AuthenticationResult>
{
    private static readonly HashSet<string> AllowedActingRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "OutletManager",
        "Cashier",
        "Salesman",
        "Stock Manager",
        "StockManager"
    };

    public async Task<AuthenticationResult> Handle(
        SwitchRoleCommand request,
        CancellationToken cancellationToken)
    {
        var user = await store.FindActiveUserForRoleSwitchAsync(request.RealUserId, cancellationToken);
        EnsureBusinessOwner(user);

        if (string.IsNullOrWhiteSpace(request.ActingRole))
        {
            throw new InvalidOperationException("ActingRole is required.");
        }

        if (!AllowedActingRoles.Contains(request.ActingRole))
        {
            throw new UnauthorizedAccessException($"Role '{request.ActingRole}' is not switchable.");
        }

        var actingRole = await store.FindRoleForSwitchAsync(request.ActingRole, cancellationToken)
            ?? throw new InvalidOperationException($"Role '{request.ActingRole}' not found.");

        AuthenticationOutlet? actingOutlet = null;
        if (request.ActingOutletId.HasValue)
        {
            actingOutlet = await store.FindOutletForSwitchAsync(
                request.ActingOutletId.Value,
                cancellationToken)
                ?? throw new InvalidOperationException($"Outlet {request.ActingOutletId} not found.");

            if (!user!.BusinessId.HasValue || actingOutlet.BusinessId != user.BusinessId)
            {
                throw new UnauthorizedAccessException("Selected outlet does not belong to your business.");
            }
        }

        var permissions = LoginCommandHandler.ParsePermissionJson(actingRole.Permissions);
        var claims = new AuthenticationActingRoleClaims(
            actingRole.Id,
            actingRole.Name,
            permissions,
            request.ActingOutletId,
            actingOutlet?.Name);
        var credentials = credentialsIssuer.IssueForRoleSwitch(
            ToAuthenticationUser(user!),
            permissions,
            claims);
        await store.PersistRefreshTokenAsync(
            user!.Id,
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
                actingRole.Name,
                permissions,
                request.ActingOutletId,
                actingOutlet?.Name,
                user.RoleName,
                IsBusinessOwner: true,
                MustResetPassword: user.MustResetPassword,
                ActingRoleName: actingRole.Name,
                ActingOutletId: request.ActingOutletId,
                ActingOutletName: actingOutlet?.Name,
                IsRoleSwitched: true));
    }

    private static void EnsureBusinessOwner(AuthenticationRoleSwitchUser? user)
    {
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("User not found or inactive.");
        }

        if (!string.Equals(user.RoleName, SystemRoleNames.BusinessOwner, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Only BusinessOwner can switch roles.");
        }
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
            permissionOverrides: null,
            user.IsActive,
            user.MustResetPassword);
}

public sealed record ReturnOwnerCommand(long RealUserId) : ICommand<AuthenticationResult>;

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

        if (!string.Equals(user.RoleName, SystemRoleNames.BusinessOwner, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Only BusinessOwner can switch roles.");
        }

        var permissions = LoginCommandHandler.ParsePermissionJson(user.RolePermissions);
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
                IsBusinessOwner: true,
                MustResetPassword: user.MustResetPassword));
    }
}
