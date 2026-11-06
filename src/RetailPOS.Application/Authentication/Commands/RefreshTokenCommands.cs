using System.Security.Claims;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Context;
using RetailPOS.Application.Authentication.Contracts;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Authentication.Commands;

public sealed class RefreshTokenCommand : ICommand<AuthenticationResult>
{
    public RefreshTokenCommand(string token, string refreshToken)
    {
        Token = token;
        RefreshToken = refreshToken;
    }

    public string Token { get; }
    public string RefreshToken { get; }
}

public sealed class RefreshTokenCommandHandler(
    IAuthenticationTokenService tokenService,
    IAuthenticationProfileStore profileStore,
    IAuthenticationCredentialsIssuer credentialsIssuer,
    IRefreshTokenStore refreshTokenStore,
    UserContextPolicy contextPolicy)
    : IRequestHandler<RefreshTokenCommand, AuthenticationResult>
{
    public async Task<AuthenticationResult> Handle(
        RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principal = tokenService.GetPrincipalFromToken(request.Token, validateLifetime: false);
        if (principal is null
            || !long.TryParse(
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                out var userId))
        {
            throw new UnauthorizedAccessException("Invalid token");
        }

        var profile = await profileStore.FindProfileAsync(userId, cancellationToken);
        if (profile is null || !profile.IsActive)
            throw new UnauthorizedAccessException("User not found or inactive");

        // A switched context survives refresh only while it is still authorized; otherwise the user
        // falls back to their own context.
        var acting = await RevalidateActingContextAsync(principal, profile.Id, cancellationToken);
        IReadOnlyList<string> permissions = acting is not null
            ? acting.Permissions
            : LoginCommandHandler.BuildEffectivePermissions(
                profile.RolePermissions,
                profile.PermissionOverrides);
        var now = DateTime.UtcNow;
        var credentials = credentialsIssuer.IssueForRefresh(profile, permissions, acting?.ToClaims());
        var rotationResult = await refreshTokenStore.RotateAsync(
            profile.Id,
            request.RefreshToken,
            credentials.RefreshToken,
            now,
            credentials.RefreshTokenExpiresAt,
            cancellationToken);

        switch (rotationResult)
        {
            case RefreshTokenRotationResult.Rotated:
                break;
            case RefreshTokenRotationResult.Revoked:
                throw new UnauthorizedAccessException(
                    "Refresh token reuse detected: token has already been revoked");
            case RefreshTokenRotationResult.Expired:
                throw new UnauthorizedAccessException("Refresh token has expired");
            default:
                throw new UnauthorizedAccessException("Invalid refresh token");
        }

        return new AuthenticationResult(
            credentials.AccessToken,
            credentials.RefreshToken,
            credentials.AccessTokenExpiresAt,
            new AuthenticationUserProfile(
                profile.Id,
                profile.BusinessId,
                profile.Name,
                profile.Email,
                acting?.Role.Name ?? profile.RoleName,
                permissions,
                acting is not null ? acting.OutletId : profile.OutletId,
                acting is not null ? acting.OutletName : profile.OutletName,
                profile.RoleName,
                string.Equals(
                    profile.RoleName,
                    SystemRoleNames.BusinessOwner,
                    StringComparison.OrdinalIgnoreCase),
                profile.MustResetPassword,
                acting?.Role.Name,
                acting?.OutletId,
                acting?.OutletName,
                acting is not null,
                acting?.LocationType,
                acting?.WarehouseId,
                acting?.WarehouseName));
    }

    private Task<ResolvedUserContext?> RevalidateActingContextAsync(
        ClaimsPrincipal principal,
        long userId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                principal.FindFirst(AuthenticationClaimNames.IsRoleSwitched)?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<ResolvedUserContext?>(null);
        }

        var outletId = ParseClaim(principal, AuthenticationClaimNames.ActingOutletId);
        return contextPolicy.RevalidateAsync(
            userId,
            principal.FindFirst(AuthenticationClaimNames.ActingRoleName)?.Value,
            principal.FindFirst(AuthenticationClaimNames.ActingLocationType)?.Value
                ?? (outletId.HasValue ? UserContextLocationTypes.Outlet : null),
            outletId ?? ParseClaim(principal, AuthenticationClaimNames.ActingWarehouseId),
            cancellationToken);
    }

    private static long? ParseClaim(ClaimsPrincipal principal, string claimType) =>
        long.TryParse(principal.FindFirst(claimType)?.Value, out var value) ? value : null;
}

public sealed class LogoutCommand : ICommand<bool>
{
    public LogoutCommand(long userId, string refreshToken)
    {
        UserId = userId;
        RefreshToken = refreshToken;
    }

    public long UserId { get; }
    public string RefreshToken { get; }
}

public sealed class LogoutCommandHandler(IRefreshTokenStore refreshTokenStore)
    : IRequestHandler<LogoutCommand, bool>
{
    public async Task<bool> Handle(
        LogoutCommand request,
        CancellationToken cancellationToken)
    {
        await refreshTokenStore.RevokeCurrentSessionAsync(
            request.UserId,
            request.RefreshToken,
            DateTime.UtcNow,
            cancellationToken);
        return true;
    }
}

public sealed record LogoutAllCommand(long UserId) : ICommand<bool>;

public sealed class LogoutAllCommandHandler(IRefreshTokenStore refreshTokenStore)
    : IRequestHandler<LogoutAllCommand, bool>
{
    public async Task<bool> Handle(
        LogoutAllCommand request,
        CancellationToken cancellationToken)
    {
        await refreshTokenStore.RevokeAllSessionsAsync(
            request.UserId,
            DateTime.UtcNow,
            cancellationToken);
        return true;
    }
}
