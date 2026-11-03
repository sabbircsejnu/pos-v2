namespace RetailPOS.Application.Authentication.Abstractions;

public enum RefreshTokenRotationResult
{
    Rotated,
    Rejected,
    Revoked,
    Expired
}

public interface IRefreshTokenStore
{
    Task<RefreshTokenRotationResult> RotateAsync(
        long userId,
        string currentRefreshToken,
        string replacementRefreshToken,
        DateTime now,
        DateTime replacementExpiresAt,
        CancellationToken cancellationToken);

    Task RevokeCurrentSessionAsync(
        long userId,
        string refreshToken,
        DateTime revokedAt,
        CancellationToken cancellationToken);

    Task RevokeAllSessionsAsync(
        long userId,
        DateTime revokedAt,
        CancellationToken cancellationToken);
}
