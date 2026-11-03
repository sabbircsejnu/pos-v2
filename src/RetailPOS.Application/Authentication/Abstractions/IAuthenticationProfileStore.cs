namespace RetailPOS.Application.Authentication.Abstractions;

public interface IAuthenticationProfileStore
{
    Task<AuthenticationProfileData?> FindProfileAsync(long userId, CancellationToken cancellationToken);
}

public sealed record AuthenticationProfileData(
    long Id,
    long? BusinessId,
    string Name,
    string Email,
    long? RoleId,
    string? RoleName,
    string? RolePermissions,
    long? OutletId,
    string? OutletName,
    string? PermissionOverrides,
    bool MustResetPassword,
    bool IsActive);
