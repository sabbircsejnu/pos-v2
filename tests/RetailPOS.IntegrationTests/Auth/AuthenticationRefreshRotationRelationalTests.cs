using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Contracts;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Authentication;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Auth;

public sealed class AuthenticationRefreshRotationRelationalTests
{
    [Fact]
    public async Task ConcurrentRefreshRequests_OnlyOneCanRotateTheCurrentToken()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"RefreshRace_{Guid.NewGuid():N}.db");
        var barrier = new RefreshRotationBarrier();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                DefaultTimeout = 30,
                Pooling = false
            }.ToString())
            .Options;
        const long userId = 92001;
        const long roleId = 92002;
        const string refreshToken = "current-refresh-secret-for-race";
        var jwtSettings = Options.Create(new JwtSettings
        {
            Secret = "refresh-rotation-relational-test-secret-32-bytes",
            Issuer = "refresh-tests",
            Audience = "refresh-tests",
            ExpirationMinutes = 60,
            RefreshTokenExpirationDays = 7
        });
        var tokenSettings = Options.Create(new JwtTokenSettings
        {
            Secret = jwtSettings.Value.Secret,
            Issuer = jwtSettings.Value.Issuer,
            Audience = jwtSettings.Value.Audience,
            ExpirationMinutes = jwtSettings.Value.ExpirationMinutes,
            RefreshTokenExpirationDays = jwtSettings.Value.RefreshTokenExpirationDays
        });
        var tokenService = new TokenService(new JwtAuthenticationTokenService(tokenSettings));

        long previousTokenId;
        string accessToken;
        try
        {
            await using (var seedDb = new RetailPOSDbContext(options))
            {
                await seedDb.Database.EnsureCreatedAsync();
                var role = new Role { Id = roleId, Name = "Cashier", Permissions = """["sales.view"]""" };
                var user = new User
                {
                    Id = userId,
                    Name = "Refresh Race User",
                    Email = "refresh-race@example.test",
                    PasswordHash = "not-used",
                    Role = role,
                    IsActive = true
                };
                var refresh = new UserRefreshToken
                {
                    UserId = userId,
                    TokenHash = HashToken(refreshToken),
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    CreatedAt = DateTime.UtcNow
                };
                seedDb.AddRange(role, user, refresh);
                await seedDb.SaveChangesAsync();
                previousTokenId = refresh.Id;
                accessToken = tokenService.GenerateAccessToken(user, role);
            }
            async Task<AuthenticationResult?> TryRefreshAsync()
            {
                await using var db = new RetailPOSDbContext(options);
                var refreshStore = new BarrierRefreshTokenStore(new RefreshTokenStore(db), barrier);
                var service = new AuthenticationCommandTestHarness(db, jwtSettings, refreshStore);
                try
                {
                    return await service.RefreshTokenAsync(new RefreshTokenRequestDto
                    {
                        Token = accessToken,
                        RefreshToken = refreshToken
                    });
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }

            var results = await Task.WhenAll(TryRefreshAsync(), TryRefreshAsync());
            var successfulResults = results.Where(result => result is not null).ToArray();
            var winner = Assert.Single(successfulResults);
            Assert.True(barrier.Arrivals >= 2);

            await using (var verifyDb = new RetailPOSDbContext(options))
            {
                var persistedTokens = await verifyDb.UserRefreshTokens
                    .Where(token => token.UserId == userId)
                    .ToListAsync();
                var previous = Assert.Single(persistedTokens, token => token.Id == previousTokenId);
                var replacement = Assert.Single(persistedTokens, token => token.RevokedAt == null);
                Assert.Collection(persistedTokens, _ => { }, _ => { });
                Assert.Equal("Rotated", previous.RevokedReason);
                Assert.Equal(HashToken(winner!.RefreshToken), previous.ReplacedByTokenHash);
                Assert.NotEqual(refreshToken, replacement.TokenHash);
                Assert.Equal(previous.ReplacedByTokenHash, replacement.TokenHash);
            }
        }
        finally
        {
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed class RefreshRotationBarrier
    {
        private readonly TaskCompletionSource _bothRequestsReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public int Arrivals => Volatile.Read(ref _arrivals);

        public async Task WaitForBothAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _bothRequestsReady.TrySetResult();
            }

            await _bothRequestsReady.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BarrierRefreshTokenStore(
        IRefreshTokenStore inner,
        RefreshRotationBarrier barrier) : IRefreshTokenStore
    {
        public async Task<RefreshTokenRotationResult> RotateAsync(
            long userId,
            string currentRefreshToken,
            string replacementRefreshToken,
            DateTime now,
            DateTime replacementExpiresAt,
            CancellationToken cancellationToken)
        {
            await barrier.WaitForBothAsync(cancellationToken);
            return await inner.RotateAsync(
                userId,
                currentRefreshToken,
                replacementRefreshToken,
                now,
                replacementExpiresAt,
                cancellationToken);
        }

        public Task RevokeCurrentSessionAsync(
            long userId,
            string refreshToken,
            DateTime revokedAt,
            CancellationToken cancellationToken) =>
            inner.RevokeCurrentSessionAsync(userId, refreshToken, revokedAt, cancellationToken);

        public Task RevokeAllSessionsAsync(
            long userId,
            DateTime revokedAt,
            CancellationToken cancellationToken) =>
            inner.RevokeAllSessionsAsync(userId, revokedAt, cancellationToken);
    }
}
