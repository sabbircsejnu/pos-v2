using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Authentication;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Auth;

public sealed class AuthenticationCommandRelationalTests
{
    [Fact]
    public async Task Register_RefreshTokenPersistenceFailureRollsBackCreatedUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection, new FailRefreshTokenSaveInterceptor());
        await using (var db = new RetailPOSDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Roles.Add(new Role { Id = 81001, Name = "Cashier", Permissions = "[]" });
            await db.SaveChangesAsync();

            var governanceStore = new Mock<IUserGovernanceStore>();
            governanceStore
                .Setup(store => store.FindActorAsync(81002, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UserGovernanceActor(81002, null, "Super Admin", true));
            var governanceContext = new Mock<IUserGovernanceContext>();
            governanceContext.Setup(context => context.RealUserId).Returns(81002);
            var handler = new RegisterCommandHandler(
                new AuthenticationStore(db),
                new BCryptPasswordHasher(),
                new StaticCredentialsIssuer(),
                governanceStore.Object,
                governanceContext.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(
                    new RegisterCommand(
                        "Atomic Registration",
                        "atomic-registration@example.test",
                        "Atomic-Password-123",
                        "Atomic-Password-123"),
                    CancellationToken.None));
        }

        await using var verifyDb = new RetailPOSDbContext(options);
        Assert.Empty(await verifyDb.Users.ToListAsync());
        Assert.Empty(await verifyDb.UserRefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task CompleteInvitation_RefreshTokenPersistenceFailureRollsBackPasswordAndConsumption()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = CreateOptions(connection, new FailRefreshTokenSaveInterceptor());
        const string token = "atomic-invitation-token";
        const long userId = 81102;
        const long invitationId = 81103;
        var originalPasswordHash = BCrypt.Net.BCrypt.HashPassword("Temporary-Password-123", workFactor: 11);

        await using (var db = new RetailPOSDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var role = new Role { Id = 81101, Name = "BusinessOwner", Permissions = "[]" };
            var user = new User
            {
                Id = userId,
                Name = "Atomic Invitation",
                Email = "atomic-invitation@example.test",
                PasswordHash = originalPasswordHash,
                Role = role,
                IsActive = false,
                MustResetPassword = true
            };
            db.AddRange(role, user, new UserInvitation
            {
                Id = invitationId,
                User = user,
                Purpose = "onboarding",
                TokenHash = HashToken(token),
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var handler = new CompleteInvitationCommandHandler(
                new AuthenticationStore(db),
                new BCryptPasswordHasher(),
                new StaticCredentialsIssuer());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(
                    new CompleteInvitationCommand(token, "Completed-Password-123"),
                    CancellationToken.None));
        }

        await using var verifyDb = new RetailPOSDbContext(options);
        var persistedUser = await verifyDb.Users.SingleAsync(user => user.Id == userId);
        Assert.Equal(originalPasswordHash, persistedUser.PasswordHash);
        Assert.False(persistedUser.IsActive);
        Assert.True(persistedUser.MustResetPassword);
        Assert.Null((await verifyDb.UserInvitations.SingleAsync(invitation => invitation.Id == invitationId)).ConsumedAt);
        Assert.Empty(await verifyDb.UserRefreshTokens.ToListAsync());
    }

    private static DbContextOptions<RetailPOSDbContext> CreateOptions(
        SqliteConnection connection,
        SaveChangesInterceptor interceptor) =>
        new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

    private static string HashToken(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token)));

    private sealed class StaticCredentialsIssuer : IAuthenticationCredentialsIssuer
    {
        public AuthenticationCredentials Issue(
            AuthenticationUser user,
            IReadOnlyCollection<string> effectivePermissions) =>
            new("access-token", "refresh-token", DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow.AddDays(7));

        public AuthenticationCredentials IssueForRefresh(
            AuthenticationProfileData profile,
            IReadOnlyCollection<string> effectivePermissions) =>
            new("access-token", "refresh-token", DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow.AddDays(7));

        public AuthenticationCredentials IssueForRoleSwitch(
            AuthenticationUser user,
            IReadOnlyCollection<string> actingPermissions,
            AuthenticationActingRoleClaims actingRole) =>
            new("access-token", "refresh-token", DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow.AddDays(7));
    }

    private sealed class FailRefreshTokenSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<UserRefreshToken>()
                .Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new InvalidOperationException("Synthetic refresh-token persistence failure.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
