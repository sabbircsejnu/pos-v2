using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Authentication;

public sealed class RefreshTokenStore(RetailPOSDbContext dbContext) : IRefreshTokenStore
{
    public async Task<RefreshTokenRotationResult> RotateAsync(
        long userId,
        string currentRefreshToken,
        string replacementRefreshToken,
        DateTime now,
        DateTime replacementExpiresAt,
        CancellationToken cancellationToken)
    {
        var currentTokenHash = HashToken(currentRefreshToken.Trim());
        var replacementTokenHash = HashToken(replacementRefreshToken);
        if (!dbContext.Database.IsRelational())
        {
            return await RotateNonRelationalAsync(
                userId,
                currentTokenHash,
                replacementTokenHash,
                now,
                replacementExpiresAt,
                cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            var claimed = await dbContext.UserRefreshTokens
                .Where(token =>
                    token.UserId == userId
                    && token.TokenHash == currentTokenHash
                    && token.RevokedAt == null
                    && token.ExpiresAt > now)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.RevokedReason, "Rotated")
                    .SetProperty(token => token.ReplacedByTokenHash, replacementTokenHash),
                    cancellationToken);

            if (claimed == 0)
            {
                var expired = await dbContext.UserRefreshTokens
                    .Where(token =>
                        token.UserId == userId
                        && token.TokenHash == currentTokenHash
                        && token.RevokedAt == null
                        && token.ExpiresAt <= now)
                    .ExecuteUpdateAsync(updates => updates
                        .SetProperty(token => token.RevokedAt, now)
                        .SetProperty(token => token.RevokedReason, "Expired"),
                        cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                if (expired > 0)
                {
                    return RefreshTokenRotationResult.Expired;
                }

                var previouslyRevoked = await dbContext.UserRefreshTokens
                    .AsNoTracking()
                    .AnyAsync(token =>
                        token.UserId == userId
                        && token.TokenHash == currentTokenHash
                        && token.RevokedAt != null,
                        cancellationToken);
                return previouslyRevoked
                    ? RefreshTokenRotationResult.Revoked
                    : RefreshTokenRotationResult.Rejected;
            }

            dbContext.UserRefreshTokens.Add(new UserRefreshToken
            {
                UserId = userId,
                TokenHash = replacementTokenHash,
                ExpiresAt = replacementExpiresAt,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RefreshTokenRotationResult.Rotated;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RevokeCurrentSessionAsync(
        long userId,
        string refreshToken,
        DateTime revokedAt,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(refreshToken.Trim());
        if (!dbContext.Database.IsRelational())
        {
            var token = await dbContext.UserRefreshTokens.FirstOrDefaultAsync(candidate =>
                candidate.UserId == userId
                && candidate.TokenHash == tokenHash
                && candidate.RevokedAt == null,
                cancellationToken);
            if (token is not null)
            {
                token.RevokedAt = revokedAt;
                token.RevokedReason = "Logout";
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        _ = await dbContext.UserRefreshTokens
            .Where(token =>
                token.UserId == userId
                && token.TokenHash == tokenHash
                && token.RevokedAt == null)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(token => token.RevokedAt, revokedAt)
                .SetProperty(token => token.RevokedReason, "Logout"),
                cancellationToken);
    }

    public async Task RevokeAllSessionsAsync(
        long userId,
        DateTime revokedAt,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var activeTokens = await dbContext.UserRefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in activeTokens)
            {
                token.RevokedAt = revokedAt;
                token.RevokedReason = "Logout";
            }
            if (activeTokens.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        _ = await dbContext.UserRefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(token => token.RevokedAt, revokedAt)
                .SetProperty(token => token.RevokedReason, "Logout"),
                cancellationToken);
    }

    private async Task<RefreshTokenRotationResult> RotateNonRelationalAsync(
        long userId,
        string currentTokenHash,
        string replacementTokenHash,
        DateTime now,
        DateTime replacementExpiresAt,
        CancellationToken cancellationToken)
    {
        var currentToken = await dbContext.UserRefreshTokens.FirstOrDefaultAsync(token =>
            token.UserId == userId && token.TokenHash == currentTokenHash,
            cancellationToken);
        if (currentToken is null)
        {
            return RefreshTokenRotationResult.Rejected;
        }

        if (currentToken.RevokedAt.HasValue)
        {
            return RefreshTokenRotationResult.Revoked;
        }

        if (currentToken.ExpiresAt <= now)
        {
            currentToken.RevokedAt = now;
            currentToken.RevokedReason = "Expired";
            await dbContext.SaveChangesAsync(cancellationToken);
            return RefreshTokenRotationResult.Expired;
        }

        currentToken.RevokedAt = now;
        currentToken.RevokedReason = "Rotated";
        currentToken.ReplacedByTokenHash = replacementTokenHash;
        dbContext.UserRefreshTokens.Add(new UserRefreshToken
        {
            UserId = userId,
            TokenHash = replacementTokenHash,
            ExpiresAt = replacementExpiresAt,
            CreatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return RefreshTokenRotationResult.Rotated;
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
