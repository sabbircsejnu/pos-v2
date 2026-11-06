namespace RetailPOS.Application.Authentication.Abstractions;

public interface IAuthenticationCredentialsIssuer
{
    AuthenticationCredentials Issue(
        AuthenticationUser user,
        IReadOnlyCollection<string> effectivePermissions);

    AuthenticationCredentials IssueForRefresh(
        AuthenticationProfileData profile,
        IReadOnlyCollection<string> effectivePermissions);

    AuthenticationCredentials IssueForRefresh(
        AuthenticationProfileData profile,
        IReadOnlyCollection<string> effectivePermissions,
        AuthenticationActingRoleClaims? actingRole);

    AuthenticationCredentials IssueForRoleSwitch(
        AuthenticationUser user,
        IReadOnlyCollection<string> actingPermissions,
        AuthenticationActingRoleClaims actingRole);
}

public sealed class AuthenticationCredentials
{
    public AuthenticationCredentials(
        string accessToken,
        string refreshToken,
        DateTime accessTokenExpiresAt,
        DateTime refreshTokenExpiresAt)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        AccessTokenExpiresAt = accessTokenExpiresAt;
        RefreshTokenExpiresAt = refreshTokenExpiresAt;
    }

    public string AccessToken { get; }
    public string RefreshToken { get; }
    public DateTime AccessTokenExpiresAt { get; }
    public DateTime RefreshTokenExpiresAt { get; }
}
