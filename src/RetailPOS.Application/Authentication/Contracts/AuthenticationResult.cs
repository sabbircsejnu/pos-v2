namespace RetailPOS.Application.Authentication.Contracts;

public sealed class AuthenticationResult
{
    public AuthenticationResult(
        string accessToken,
        string refreshToken,
        DateTime expiresAt,
        AuthenticationUserProfile user)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAt = expiresAt;
        User = user;
    }

    public string AccessToken { get; }
    public string RefreshToken { get; }
    public DateTime ExpiresAt { get; }
    public AuthenticationUserProfile User { get; }
}

public sealed record AuthenticationUserProfile(
    long Id,
    long? BusinessId,
    string Name,
    string Email,
    string? RoleName,
    IReadOnlyList<string> Permissions,
    long? OutletId,
    string? OutletName,
    string? RealRoleName,
    bool IsBusinessOwner,
    bool MustResetPassword,
    string? ActingRoleName = null,
    long? ActingOutletId = null,
    string? ActingOutletName = null,
    bool IsRoleSwitched = false);
