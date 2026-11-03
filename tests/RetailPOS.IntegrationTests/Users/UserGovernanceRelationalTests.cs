using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Commands;
using RetailPOS.Application.Identity.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Identity;

namespace RetailPOS.Tests.Users;

public sealed class UserGovernanceRelationalTests : IDisposable
{
    private const long BusinessId = 82001;
    private const long OrdinaryRoleId = 82002;
    private const long SuperAdminRoleId = 82003;
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"user-governance-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<RetailPOSDbContext> _options;

    public UserGovernanceRelationalTests()
    {
        _options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite($"Data Source={_databasePath};Default Timeout=10")
            .Options;
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task CreateAsync_AuditFailureRollsBackUserAndAccessAssignments()
    {
        await SeedAsync(
            new Outlet { Id = 82010, BusinessId = BusinessId, Name = "Assigned outlet" });
        var audit = CreateAuditService();
        audit.Setup(service => service.RecordAsync(
                It.Is<AuditEventInput>(input => input.Summary.StartsWith("User created:", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Synthetic user audit failure"));

        using (var db = CreateDbContext())
        {
            var store = CreateStore(db, audit.Object);
            var profile = new UserGovernanceProfile(
                "Created User",
                "create-rollback@example.test",
                OrdinaryRoleId,
                null,
                [],
                [82010],
                "outlet",
                82010,
                User.InventoryAccessSpecific,
                BusinessId,
                ["users.view"],
                true);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ExecuteSerializableAsync(
                    token => store.CreateAsync(profile, "test-password", BusinessId, 82020, token),
                    CancellationToken.None));
        }

        using var verifyDb = CreateDbContext();
        Assert.Null(await verifyDb.Users.SingleOrDefaultAsync(user =>
            user.Email == "create-rollback@example.test"));
        Assert.Empty(await verifyDb.UserOutletAssignments.ToListAsync());
        Assert.Empty(await verifyDb.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_AuditFailureRollsBackUserAndReplacedAssignments()
    {
        await SeedAsync(
            new Outlet { Id = 82010, BusinessId = BusinessId, Name = "Old outlet" },
            new Outlet { Id = 82011, BusinessId = BusinessId, Name = "New outlet" },
            new User
            {
                Id = 82020,
                BusinessId = BusinessId,
                Name = "Before",
                Email = "before-update@example.test",
                PasswordHash = "not-used",
                RoleId = OrdinaryRoleId,
                OutletId = 82010,
                IsActive = true
            },
            new UserOutletAssignment
            {
                UserId = 82020,
                OutletId = 82010,
                BusinessId = BusinessId,
                IsPrimary = true,
                IsActive = true
            });
        var audit = CreateAuditService();
        audit.Setup(service => service.RecordAsync(
                It.Is<AuditEventInput>(input => input.Summary.StartsWith("User access updated:", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Synthetic user audit failure"));

        using (var db = CreateDbContext())
        {
            var store = CreateStore(db, audit.Object);
            var profile = new UserGovernanceProfile(
                "After",
                "after-update@example.test",
                OrdinaryRoleId,
                82011,
                [],
                [82011],
                "outlet",
                82011,
                User.InventoryAccessSpecific,
                BusinessId,
                ["users.edit"],
                true);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ExecuteSerializableAsync(
                    token => store.UpdateAsync(82020, profile, false, 82021, token),
                    CancellationToken.None));
        }

        using var verifyDb = CreateDbContext();
        var user = await verifyDb.Users.SingleAsync(candidate => candidate.Id == 82020);
        Assert.Equal("before-update@example.test", user.Email);
        Assert.Equal(82010, user.OutletId);
        var outletIds = await verifyDb.UserOutletAssignments
            .Where(assignment => assignment.UserId == user.Id)
            .Select(assignment => assignment.OutletId)
            .ToArrayAsync();
        Assert.Equal(new[] { 82010L }, outletIds);
        Assert.Empty(await verifyDb.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task CompetingSuperAdminDemotions_CannotLeaveZeroActiveSuperAdmins()
    {
        await SeedAsync(
            new User
            {
                Id = 82020,
                Name = "Super Admin One",
                Email = "super-one@example.test",
                PasswordHash = "not-used",
                RoleId = SuperAdminRoleId,
                IsActive = true
            },
            new User
            {
                Id = 82021,
                Name = "Super Admin Two",
                Email = "super-two@example.test",
                PasswordHash = "not-used",
                RoleId = SuperAdminRoleId,
                IsActive = true
            });

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Task.Run(async () =>
        {
            await release.Task;
            return await TryDemoteAsync(82020, 82021);
        });
        var second = Task.Run(async () =>
        {
            await release.Task;
            return await TryDemoteAsync(82021, 82020);
        });
        release.SetResult();
        var succeeded = await Task.WhenAll(first, second);

        using var verifyDb = CreateDbContext();
        var activeSuperAdmins = await verifyDb.Users.LongCountAsync(user =>
            user.IsActive
            && user.Role != null
            && user.Role.Name.ToLower() == "super admin");
        Assert.True(activeSuperAdmins >= 1);
        Assert.False(succeeded.All(result => result));
    }

    private async Task<bool> TryDemoteAsync(long actorId, long targetId)
    {
        using var db = CreateDbContext();
        var store = CreateStore(db, CreateAuditService().Object);
        var handler = new UpdateUserCommandHandler(store, new TestGovernanceContext(actorId));
        try
        {
            await handler.Handle(
                new UpdateUserCommand(
                    targetId,
                    new UserGovernanceProfile(
                        "Updated Super Admin",
                        $"demoted-{targetId}@example.test",
                        OrdinaryRoleId,
                        null,
                        [],
                        [],
                        null,
                        null,
                        User.InventoryAccessAll,
                        BusinessId,
                        [],
                        true)),
                CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (
            exception is DbUpdateException
            or SqliteException
            or InvalidOperationException
            or UnauthorizedAccessException
            or KeyNotFoundException)
        {
            return false;
        }
    }

    private async Task SeedAsync(params object[] entities)
    {
        using var db = CreateDbContext();
        db.Businesses.Add(new Business
        {
            Id = BusinessId,
            Name = "User governance relational business",
            IsActive = true
        });
        db.Roles.AddRange(
            new Role { Id = OrdinaryRoleId, Name = "AccountsAdmin", Permissions = "[]" },
            new Role { Id = SuperAdminRoleId, Name = "Super Admin", Permissions = """["*"]""" });
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private RetailPOSDbContext CreateDbContext() => new(_options);

    private static UserGovernanceStore CreateStore(RetailPOSDbContext db, IAuditService audit) =>
        new(db, new IdentityReadStore(db), audit);

    private static Mock<IAuditService> CreateAuditService()
    {
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return audit;
    }

    private sealed class TestGovernanceContext(long realUserId) : IUserGovernanceContext
    {
        public long? RealUserId { get; } = realUserId;
    }
}
