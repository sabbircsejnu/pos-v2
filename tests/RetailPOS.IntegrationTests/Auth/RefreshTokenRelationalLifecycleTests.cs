using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Authentication;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Auth;

public sealed class RefreshTokenRelationalLifecycleTests
{
    [Fact]
    public async Task Rotation_ReusesNoTokenAndRollsBackClaimWhenReplacementInsertFails()
    {
        await using var database = new SqliteAuthDatabase();
        var now = DateTime.UtcNow;
        const long userId = 93001;
        const string currentToken = "relational-current-token";
        const string duplicateReplacement = "already-persisted-token";
        const string replacement = "valid-replacement-token";

        await using (var db = new RetailPOSDbContext(database.Options))
        {
            var (role, user) = CreateUser(userId, 93002, "rotation@example.test");
            db.AddRange(
                role,
                user,
                CreateRefreshToken(userId, currentToken, now.AddHours(1), now),
                CreateRefreshToken(userId, "rollback-current-token", now.AddHours(1), now),
                CreateRefreshToken(userId, duplicateReplacement, now.AddHours(1), now));
            await db.SaveChangesAsync();
        }

        await using (var db = new RetailPOSDbContext(database.Options))
        {
            var store = new RefreshTokenStore(db);
            var result = await store.RotateAsync(
                userId, currentToken, replacement, now, now.AddDays(7), CancellationToken.None);
            Assert.Equal(RefreshTokenRotationResult.Rotated, result);

            var previous = await db.UserRefreshTokens.SingleAsync(token =>
                token.TokenHash == HashToken(currentToken));
            var replacementRow = await db.UserRefreshTokens.SingleAsync(token =>
                token.TokenHash == HashToken(replacement));
            Assert.Equal("Rotated", previous.RevokedReason);
            Assert.Equal(replacementRow.TokenHash, previous.ReplacedByTokenHash);
            Assert.Matches("^[0-9A-F]{64}$", replacementRow.TokenHash);
            Assert.NotEqual(replacement, replacementRow.TokenHash);
            Assert.Null(replacementRow.RevokedAt);

            var reuseResult = await store.RotateAsync(
                userId, currentToken, "second-replacement", now, now.AddDays(7), CancellationToken.None);
            Assert.Equal(RefreshTokenRotationResult.Revoked, reuseResult);
            Assert.Equal(4, await db.UserRefreshTokens.CountAsync());

            await Assert.ThrowsAsync<DbUpdateException>(() => store.RotateAsync(
                userId,
                "rollback-current-token",
                duplicateReplacement,
                now,
                now.AddDays(7),
                CancellationToken.None));
        }

        await using (var verifyDb = new RetailPOSDbContext(database.Options))
        {
            var original = Assert.Single(await verifyDb.UserRefreshTokens.Where(token =>
                token.TokenHash == HashToken(currentToken)).ToListAsync());
            var rollbackCandidate = Assert.Single(await verifyDb.UserRefreshTokens.Where(token =>
                token.TokenHash == HashToken("rollback-current-token")).ToListAsync());
            Assert.NotNull(original.RevokedAt);
            Assert.Equal("Rotated", original.RevokedReason);
            Assert.Null(rollbackCandidate.RevokedAt);
            Assert.Null(rollbackCandidate.ReplacedByTokenHash);
        }
    }

    [Fact]
    public async Task Refresh_RelationallyRejectsExpiredAndInactiveSessionsAndRecomputesPermissions()
    {
        await using var database = new SqliteAuthDatabase();
        var options = CreateJwtOptions();
        var tokenService = CreateTokenService(options);
        var now = DateTime.UtcNow;
        const long activeUserId = 93101;
        const long inactiveUserId = 93103;
        const string currentToken = "permission-refresh-token";
        const string expiredToken = "expired-refresh-token";
        const string inactiveToken = "inactive-user-refresh-token";

        await using (var db = new RetailPOSDbContext(database.Options))
        {
            var (role, activeUser) = CreateUser(activeUserId, 93102, "permissions@example.test");
            role.Permissions = """["sales.view"]""";
            var (inactiveRole, inactiveUser) = CreateUser(
                inactiveUserId, 93104, "inactive-refresh@example.test", isActive: false);
            db.AddRange(
                role,
                activeUser,
                inactiveRole,
                inactiveUser,
                CreateRefreshToken(activeUserId, currentToken, now.AddHours(1), now),
                CreateRefreshToken(activeUserId, expiredToken, now.AddMinutes(-1), now),
                CreateRefreshToken(inactiveUserId, inactiveToken, now.AddHours(1), now));
            await db.SaveChangesAsync();
            role.Permissions = """["sales.create"]""";
            await db.SaveChangesAsync();

            var authService = CreateAuthenticationCommandHarness(db, options);
            var activeAccessToken = tokenService.GenerateAccessToken(activeUser, role);
            var refreshed = await authService.RefreshTokenAsync(new RefreshTokenRequestDto
            {
                Token = activeAccessToken,
                RefreshToken = currentToken
            });

            Assert.Equal(64, Convert.FromBase64String(refreshed.RefreshToken).Length);
            Assert.Equal("sales.create", Assert.Single(refreshed.User.Permissions));
            Assert.Equal(HashToken(refreshed.RefreshToken),
                (await db.UserRefreshTokens.AsNoTracking().SingleAsync(token =>
                    token.TokenHash == HashToken(currentToken))).ReplacedByTokenHash);
            Assert.Contains(
                new JwtSecurityTokenHandler().ReadJwtToken(refreshed.AccessToken).Claims,
                claim => claim.Type == "permission" && claim.Value == "sales.create");

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                authService.RefreshTokenAsync(new RefreshTokenRequestDto
                {
                    Token = refreshed.AccessToken,
                    RefreshToken = currentToken
                }));

            var expiredAccessToken = tokenService.GenerateAccessToken(activeUser, role);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                authService.RefreshTokenAsync(new RefreshTokenRequestDto
                {
                    Token = expiredAccessToken,
                    RefreshToken = expiredToken
                }));
            var expiredRow = await db.UserRefreshTokens.AsNoTracking().SingleAsync(token =>
                token.TokenHash == HashToken(expiredToken));
            Assert.Equal("Expired", expiredRow.RevokedReason);

            var inactiveAccessToken = tokenService.GenerateAccessToken(inactiveUser, inactiveRole);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                authService.RefreshTokenAsync(new RefreshTokenRequestDto
                {
                    Token = inactiveAccessToken,
                    RefreshToken = inactiveToken
                }));
            Assert.Null((await db.UserRefreshTokens.AsNoTracking().SingleAsync(token =>
                token.TokenHash == HashToken(inactiveToken))).RevokedAt);
        }
    }

    [Fact]
    public async Task Logout_RevokesOnlyOwnedSessionIsIdempotentAndAllDeviceLogoutIsSeparate()
    {
        await using var database = new SqliteAuthDatabase();
        var options = CreateJwtOptions();
        var tokenService = CreateTokenService(options);
        var now = DateTime.UtcNow;
        const long firstUserId = 93201;
        const long secondUserId = 93203;
        const string firstSession = "first-user-session-one";
        const string secondSession = "first-user-session-two";
        const string otherUserSession = "second-user-session";

        await using (var db = new RetailPOSDbContext(database.Options))
        {
            var (firstRole, firstUser) = CreateUser(firstUserId, 93202, "logout-one@example.test");
            var (secondRole, secondUser) = CreateUser(secondUserId, 93204, "logout-two@example.test");
            db.AddRange(
                firstRole,
                firstUser,
                secondRole,
                secondUser,
                CreateRefreshToken(firstUserId, firstSession, now.AddHours(1), now),
                CreateRefreshToken(firstUserId, secondSession, now.AddHours(1), now),
                CreateRefreshToken(secondUserId, otherUserSession, now.AddHours(1), now));
            await db.SaveChangesAsync();

            var firstUserAccessToken = tokenService.GenerateAccessToken(firstUser, firstRole);
            var authService = CreateAuthenticationCommandHarness(db, options);
            Assert.True(await authService.LogoutAsync(firstUserId, secondSession));
            Assert.True(await authService.LogoutAsync(firstUserId, secondSession));
            Assert.True(await authService.LogoutAsync(secondUserId, firstSession));

            var firstSessionRow = await db.UserRefreshTokens.AsNoTracking().SingleAsync(token =>
                token.TokenHash == HashToken(firstSession));
            var secondSessionRow = await db.UserRefreshTokens.AsNoTracking().SingleAsync(token =>
                token.TokenHash == HashToken(secondSession));
            var otherUserRow = await db.UserRefreshTokens.AsNoTracking().SingleAsync(token =>
                token.TokenHash == HashToken(otherUserSession));
            Assert.Null(firstSessionRow.RevokedAt);
            Assert.Equal("Logout", secondSessionRow.RevokedReason);
            Assert.Null(otherUserRow.RevokedAt);
            Assert.True(tokenService.ValidateToken(firstUserAccessToken));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                authService.RefreshTokenAsync(new RefreshTokenRequestDto
                {
                    Token = firstUserAccessToken,
                    RefreshToken = secondSession
                }));

            await authService.LogoutAllAsync(firstUserId);
            Assert.All(await db.UserRefreshTokens.AsNoTracking()
                    .Where(token => token.UserId == firstUserId).ToListAsync(),
                token => Assert.NotNull(token.RevokedAt));
            Assert.Null((await db.UserRefreshTokens.AsNoTracking().SingleAsync(token =>
                token.TokenHash == HashToken(otherUserSession))).RevokedAt);
        }
    }

    [Fact]
    public async Task RefreshTokenRelationalConstraints_EnforceUniqueHashAndUserOwnership()
    {
        await using var database = new SqliteAuthDatabase();
        var now = DateTime.UtcNow;
        const long userId = 93301;
        const string token = "unique-refresh-token";

        await using (var db = new RetailPOSDbContext(database.Options))
        {
            var (role, user) = CreateUser(userId, 93302, "constraints@example.test");
            db.AddRange(role, user, CreateRefreshToken(userId, token, now.AddHours(1), now));
            await db.SaveChangesAsync();
        }

        await using (var db = new RetailPOSDbContext(database.Options))
        {
            db.UserRefreshTokens.Add(CreateRefreshToken(userId, token, now.AddHours(1), now));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = new RetailPOSDbContext(database.Options))
        {
            db.UserRefreshTokens.Add(CreateRefreshToken(userId + 1000, "orphan-token", now.AddHours(1), now));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private static (Role Role, User User) CreateUser(
        long userId,
        long roleId,
        string email,
        bool isActive = true)
    {
        var role = new Role { Id = roleId, Name = $"Cashier{roleId}", Permissions = "[]" };
        var user = new User
        {
            Id = userId,
            Name = email,
            Email = email,
            PasswordHash = "not-used",
            Role = role,
            IsActive = isActive
        };
        return (role, user);
    }

    private static UserRefreshToken CreateRefreshToken(long userId, string token, DateTime expiresAt, DateTime now) =>
        new()
        {
            UserId = userId,
            TokenHash = HashToken(token),
            ExpiresAt = expiresAt,
            CreatedAt = now
        };

    private static AuthenticationCommandTestHarness CreateAuthenticationCommandHarness(
        RetailPOSDbContext db,
        IOptions<JwtSettings> options) =>
        new(db, options);

    private static TokenService CreateTokenService(IOptions<JwtSettings> options) =>
        new(new JwtAuthenticationTokenService(Options.Create(new JwtTokenSettings
        {
            Secret = options.Value.Secret,
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            ExpirationMinutes = options.Value.ExpirationMinutes,
            RefreshTokenExpirationDays = options.Value.RefreshTokenExpirationDays
        })));

    private static IOptions<JwtSettings> CreateJwtOptions() =>
        Options.Create(new JwtSettings
        {
            Secret = "relational-refresh-test-secret-at-least-32-bytes",
            Issuer = "relational-refresh-tests",
            Audience = "relational-refresh-tests",
            ExpirationMinutes = 30,
            RefreshTokenExpirationDays = 7
        });

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed class SqliteAuthDatabase : IAsyncDisposable
    {
        private readonly string _databasePath = Path.Combine(
            Path.GetTempPath(), $"RefreshLifecycle_{Guid.NewGuid():N}.db");

        public DbContextOptions<RetailPOSDbContext> Options { get; }

        public SqliteAuthDatabase()
        {
            Options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    DefaultTimeout = 30,
                    Pooling = false
                }.ToString())
                .Options;
            using var db = new RetailPOSDbContext(Options);
            db.Database.EnsureCreated();
        }

        public async ValueTask DisposeAsync()
        {
            await using (var db = new RetailPOSDbContext(Options))
            {
                await db.Database.EnsureDeletedAsync();
            }
            foreach (var path in new[] { _databasePath, $"{_databasePath}-shm", $"{_databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
