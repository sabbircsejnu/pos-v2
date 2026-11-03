using System.Security.Claims;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public sealed class TokenService(IAuthenticationTokenService tokenService) : ITokenService
{
    public string GenerateAccessToken(
        User user,
        Role? role,
        ActingRoleClaims? acting = null,
        IReadOnlyCollection<string>? effectivePermissions = null)
    {
        var tokenUser = new AuthenticationTokenUser(
            user.Id,
            user.Name,
            user.Email,
            user.BusinessId,
            user.OutletId);
        var tokenRole = role is null
            ? null
            : new AuthenticationTokenRole(role.Id, role.Name, role.Permissions);
        var actingClaims = acting is null
            ? null
            : new AuthenticationActingRoleClaims(
                acting.ActingRoleId,
                acting.ActingRoleName,
                acting.ActingPermissions,
                acting.ActingOutletId,
                acting.ActingOutletName);

        return tokenService.GenerateAccessToken(tokenUser, tokenRole, actingClaims, effectivePermissions);
    }

    public string GenerateRefreshToken() => tokenService.GenerateRefreshToken();

    public bool ValidateToken(string token) =>
        tokenService.GetPrincipalFromToken(token, validateLifetime: true) is not null;

    public ClaimsPrincipal? GetPrincipalFromToken(string token, bool validateLifetime) =>
        tokenService.GetPrincipalFromToken(token, validateLifetime);
}
