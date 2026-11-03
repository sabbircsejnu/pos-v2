using System.Security.Claims;

namespace RetailPOS.Application.Authentication.Abstractions;

public interface IAuthenticationTokenService
{
    string GenerateAccessToken(
        AuthenticationTokenUser user,
        AuthenticationTokenRole? role,
        AuthenticationActingRoleClaims? acting = null,
        IReadOnlyCollection<string>? effectivePermissions = null);

    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromToken(string token, bool validateLifetime);
}

public sealed record AuthenticationTokenUser(
    long Id,
    string Name,
    string Email,
    long? BusinessId,
    long? OutletId);

public sealed record AuthenticationTokenRole(long Id, string Name, string Permissions);

public sealed record AuthenticationActingRoleClaims(
    long ActingRoleId,
    string ActingRoleName,
    IReadOnlyList<string> ActingPermissions,
    long? ActingOutletId,
    string? ActingOutletName);

public static class AuthenticationClaimNames
{
    public const string ActingRoleId = "acting_role_id";
    public const string ActingRoleName = "acting_role";
    public const string ActingOutletId = "acting_outlet_id";
    public const string ActingOutletName = "acting_outlet_name";
    public const string IsRoleSwitched = "is_role_switched";
    public const string RealRoleId = "real_role_id";
    public const string RealRoleName = "real_role";
}
