using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BusinessAdministration.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.BusinessAdministration;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.IntegrationTests.BusinessAdministration;

public sealed class BusinessOwnerAccessResetStoreTests : IAsyncLifetime
{
    private readonly string _connectionString =
        $"Data Source=business-owner-reset-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True";
    private SqliteConnection _anchorConnection = null!;

    public async Task InitializeAsync()
    {
        _anchorConnection = new SqliteConnection(_connectionString);
        await _anchorConnection.OpenAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        context.Businesses.AddRange(
            new Business { Id = 1, Name = "Owner reset business" },
            new Business { Id = 2, Name = "Business without owner" });
        context.Roles.Add(new Role { Id = 10, Name = "BusinessOwner" });
        context.Users.Add(new User
        {
            Id = 11,
            BusinessId = 1,
            RoleId = 10,
            Name = "Owner",
            Email = "owner@example.test"
        });
        context.UserInvitations.AddRange(
            new UserInvitation
            {
                Id = 20,
                UserId = 11,
                Purpose = "onboarding",
                TokenHash = "active-hash",
                ExpiresAt = new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            },
            new UserInvitation
            {
                Id = 21,
                UserId = 11,
                Purpose = "expired",
                TokenHash = "expired-hash",
                ExpiresAt = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ResetOwnerAccess_ConsumesAllActiveInvitationsAndPersistsHashedReplacement()
    {
        await using var context = CreateContext();
        var store = new BusinessAdministrationWriteStore(context);
        var now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var expiresAt = now.AddDays(7);
        var tokenHash = new string('A', 64);

        var result = await store.ResetBusinessOwnerAccessAsync(
            1,
            tokenHash,
            now,
            expiresAt,
            CancellationToken.None);

        Assert.Equal(BusinessOwnerAccessResetStatus.Reset, result.Status);
        Assert.Equal(11, result.UserId);
        Assert.Equal("owner@example.test", result.OwnerEmail);
        var owner = await context.Users.AsNoTracking().SingleAsync(user => user.Id == 11);
        Assert.True(owner.MustResetPassword);
        Assert.Equal(now, owner.UpdatedAt);

        var invitations = await context.UserInvitations.AsNoTracking()
            .Where(invitation => invitation.UserId == 11)
            .ToListAsync();
        Assert.Equal(now, invitations.Single(invitation => invitation.Id == 20).ConsumedAt);
        Assert.Null(invitations.Single(invitation => invitation.Id == 21).ConsumedAt);
        var replacement = invitations.Single(invitation => invitation.Purpose == "owner-reset");
        Assert.Equal(tokenHash, replacement.TokenHash);
        Assert.Equal(now, replacement.CreatedAt);
        Assert.Equal(expiresAt, replacement.ExpiresAt);

        var missingBusiness = await store.ResetBusinessOwnerAccessAsync(
            999,
            tokenHash,
            now,
            expiresAt,
            CancellationToken.None);
        Assert.Equal(BusinessOwnerAccessResetStatus.BusinessNotFound, missingBusiness.Status);
        var missingOwner = await store.ResetBusinessOwnerAccessAsync(
            2,
            tokenHash,
            now,
            expiresAt,
            CancellationToken.None);
        Assert.Equal(BusinessOwnerAccessResetStatus.OwnerNotFound, missingOwner.Status);
    }

    [Fact]
    public async Task ResetOwnerAccess_RollsBackOwnerAndInvitationChangesWhenInsertFails()
    {
        DateTime originalUpdatedAt;
        await using (var context = CreateContext())
        {
            originalUpdatedAt = await context.Users.AsNoTracking()
                .Where(user => user.Id == 11)
                .Select(user => user.UpdatedAt)
                .SingleAsync();
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER fail_owner_reset_invitation
                BEFORE INSERT ON "user_invitations"
                BEGIN
                    SELECT RAISE(ABORT, 'forced owner reset failure');
                END;
                """);
            var store = new BusinessAdministrationWriteStore(context);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                store.ResetBusinessOwnerAccessAsync(
                    1,
                    new string('B', 64),
                    new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc),
                    CancellationToken.None));
        }

        await using var verifyContext = CreateContext();
        var owner = await verifyContext.Users.AsNoTracking().SingleAsync(user => user.Id == 11);
        Assert.False(owner.MustResetPassword);
        Assert.Equal(originalUpdatedAt, owner.UpdatedAt);
        var invitations = await verifyContext.UserInvitations.AsNoTracking()
            .Where(invitation => invitation.UserId == 11)
            .ToListAsync();
        Assert.Null(invitations.Single(invitation => invitation.Id == 20).ConsumedAt);
        Assert.Null(invitations.Single(invitation => invitation.Id == 21).ConsumedAt);
        Assert.DoesNotContain(invitations, invitation => invitation.Purpose == "owner-reset");
    }

    public async Task DisposeAsync()
    {
        await _anchorConnection.DisposeAsync();
    }

    private RetailPOSDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connectionString)
            .Options);
}
