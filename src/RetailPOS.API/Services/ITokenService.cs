using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public sealed record ActingRoleClaims(
    long ActingRoleId,
    string ActingRoleName,
    IReadOnlyList<string> ActingPermissions,
    long? ActingOutletId,
    string? ActingOutletName);

public interface ITokenService
{
    string GenerateAccessToken(User user, Role? role, ActingRoleClaims? acting = null);
    string GenerateRefreshToken();
    bool ValidateToken(string token);
}
