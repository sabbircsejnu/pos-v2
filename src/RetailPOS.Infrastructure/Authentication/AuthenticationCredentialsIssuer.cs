using Microsoft.Extensions.Options;
using RetailPOS.Application.Authentication.Abstractions;

namespace RetailPOS.Infrastructure.Authentication;

public sealed class AuthenticationCredentialsIssuer(
    IAuthenticationTokenService tokenService,
    IOptions<JwtTokenSettings> settings) : IAuthenticationCredentialsIssuer
{
    public AuthenticationCredentials Issue(
        AuthenticationUser user,
        IReadOnlyCollection<string> effectivePermissions) =>
        Issue(user, effectivePermissions, actingRole: null);

    public AuthenticationCredentials IssueForRoleSwitch(
        AuthenticationUser user,
        IReadOnlyCollection<string> actingPermissions,
        AuthenticationActingRoleClaims actingRole) =>
        Issue(user, actingPermissions, actingRole);

    public AuthenticationCredentials IssueForRefresh(
        AuthenticationProfileData profile,
        IReadOnlyCollection<string> effectivePermissions)
    {
        AuthenticationTokenRole? role = profile.RoleId.HasValue && profile.RoleName is not null
            ? new AuthenticationTokenRole(
                profile.RoleId.Value,
                profile.RoleName,
                profile.RolePermissions ?? "[]")
            : null;

        return Issue(
            new AuthenticationTokenUser(
                profile.Id,
                profile.Name,
                profile.Email,
                profile.BusinessId,
                profile.OutletId),
            role,
            actingRole: null,
            effectivePermissions,
            DateTime.UtcNow);
    }

    private AuthenticationCredentials Issue(
        AuthenticationUser user,
        IReadOnlyCollection<string> effectivePermissions,
        AuthenticationActingRoleClaims? actingRole)
    {
        AuthenticationTokenRole? role = user.RoleId.HasValue && user.RoleName is not null
            ? new AuthenticationTokenRole(
                user.RoleId.Value,
                user.RoleName,
                user.RolePermissions ?? "[]")
            : null;

        return Issue(
            new AuthenticationTokenUser(
                user.Id,
                user.Name,
                user.Email,
                user.BusinessId,
                user.OutletId),
            role,
            actingRole,
            effectivePermissions,
            DateTime.UtcNow);
    }

    private AuthenticationCredentials Issue(
        AuthenticationTokenUser user,
        AuthenticationTokenRole? role,
        AuthenticationActingRoleClaims? actingRole,
        IReadOnlyCollection<string> effectivePermissions,
        DateTime now)
    {
        var accessToken = tokenService.GenerateAccessToken(
            user,
            role,
            actingRole,
            effectivePermissions: effectivePermissions);
        var refreshToken = tokenService.GenerateRefreshToken();

        return new AuthenticationCredentials(
            accessToken,
            refreshToken,
            now.AddMinutes(settings.Value.ExpirationMinutes),
            now.AddDays(settings.Value.RefreshTokenExpirationDays));
    }
}
