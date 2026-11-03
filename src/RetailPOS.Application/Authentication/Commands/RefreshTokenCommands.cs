using System.Security.Claims;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Authentication.Abstractions;
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
    IRefreshTokenStore refreshTokenStore)
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

        var permissions = LoginCommandHandler.BuildEffectivePermissions(
            profile.RolePermissions,
            profile.PermissionOverrides);
        var now = DateTime.UtcNow;
        var credentials = credentialsIssuer.IssueForRefresh(profile, permissions);
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
                profile.RoleName,
                permissions,
                profile.OutletId,
                profile.OutletName,
                profile.RoleName,
                string.Equals(
                    profile.RoleName,
                    SystemRoleNames.BusinessOwner,
                    StringComparison.OrdinalIgnoreCase),
                profile.MustResetPassword));
    }
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
