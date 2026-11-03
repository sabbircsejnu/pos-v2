using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Employees;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrEmployeeUserLinkPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext _auditContext = new();
    private readonly FailAuditInterceptor _failure = new();
    private RetailPOSDbContext _db = null!;
    private RequiredAuditTransaction _auditTransaction = null!;
    private long _employeeId;
    private long _otherEmployeeId;
    private long _crossBusinessEmployeeId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(
                new AuditChangeTrackerInterceptor(_auditContext,
                    NullLogger<AuditChangeTrackerInterceptor>.Instance),
                _failure)
            .Options);
        await _db.Database.EnsureCreatedAsync();
        _db.Businesses.AddRange(
            new Business { Id = 1, Name = "Link Business" },
            new Business { Id = 2, Name = "Other Link Business" });
        _db.HrEmploymentTypes.AddRange(
            new HrEmploymentType { Id = 11, BusinessId = 1, Code = "Permanent", Name = "Permanent" },
            new HrEmploymentType { Id = 21, BusinessId = 2, Code = "Permanent", Name = "Permanent" });
        _db.Users.AddRange(
            User(101, 1, "Link User"),
            User(102, 1, "Second Link User"),
            User(201, 2, "Other Business User"),
            User(202, 1, "Inactive User", false));
        await _db.SaveChangesAsync();
        var first = Employee(1, 11, "EMP-LINK-1");
        var second = Employee(1, 11, "EMP-LINK-2");
        var third = Employee(2, 21, "EMP-LINK-3");
        _db.HrEmployees.AddRange(first, second, third);
        await _db.SaveChangesAsync();
        _employeeId = first.Id;
        _otherEmployeeId = second.Id;
        _crossBusinessEmployeeId = third.Id;

        _auditContext.Initialize(new AuditContextSnapshot(
            Guid.NewGuid(), 101, 101, 5, 7, null, 1, AuditSource.API,
            "POST", "/api/hr/v1/employees", null, null, null, false,
            null, null, null, null, "employee-user-link-test", "HR"));
        var auditService = new AuditService(
            _db, _auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
        _auditTransaction = new RequiredAuditTransaction(
            _db, _auditContext, auditService, NullLogger<RequiredAuditTransaction>.Instance);
    }

    [Fact]
    public async Task LinkCurrentResolutionUnlinkAndRelinkPreserveHistoryWithoutIdentityForeignKey()
    {
        var loginUserBefore = await _db.Users.AsNoTracking().SingleAsync(user => user.Id == 101);
        var handler = LinkHandler();
        var linked = await handler.Handle(
            new LinkHrEmployeeUserCommand(_employeeId, 101, "Employee requires HR context."),
            CancellationToken.None);
        Assert.NotNull(linked);
        Assert.True(linked.IsActive);
        Assert.Equal(101, linked.LinkedByUserId);
        var loginUserAfter = await _db.Users.AsNoTracking().SingleAsync(user => user.Id == 101);
        Assert.Equal(loginUserBefore.RoleId, loginUserAfter.RoleId);
        Assert.Equal(loginUserBefore.PermissionOverrides, loginUserAfter.PermissionOverrides);

        await _db.Users.Where(user => user.Id == 101)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.IsActive, false));
        Assert.Single(await _db.HrEmployeeUserLinks.AsNoTracking()
            .Where(link => link.EmployeeId == _employeeId && link.IsActive).ToArrayAsync());
        await _db.Users.Where(user => user.Id == 101)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.IsActive, true));

        var self = await new GetCurrentHrEmployeeQueryHandler(
            Access(), new HrEmployeeStore(_db), new IdentityUserDirectory(_db))
            .Handle(new GetCurrentHrEmployeeQuery(), CancellationToken.None);
        Assert.Equal(_employeeId, self!.Id);

        var unlink = new UnlinkHrEmployeeUserCommand(_employeeId, linked.Version, "Access no longer required.");
        var unlinked = await new UnlinkHrEmployeeUserCommandHandler(
            Access(), new HrEmployeeStore(_db), _auditTransaction)
            .Handle(unlink, CancellationToken.None);
        Assert.NotNull(unlinked);
        Assert.False(unlinked.IsActive);
        Assert.Equal("Access no longer required.", unlinked.UnlinkReason);
        Assert.Null(await new GetCurrentHrEmployeeQueryHandler(
            Access(), new HrEmployeeStore(_db), new IdentityUserDirectory(_db))
            .Handle(new GetCurrentHrEmployeeQuery(), CancellationToken.None));

        var relinked = await handler.Handle(
            new LinkHrEmployeeUserCommand(_employeeId, 101, "Relink after role correction."),
            CancellationToken.None);
        var history = await _db.HrEmployeeUserLinks.AsNoTracking()
            .Where(row => row.EmployeeId == _employeeId).OrderBy(row => row.Id).ToArrayAsync();
        Assert.NotNull(relinked);
        Assert.Equal(2, history.Length);
        Assert.False(history[0].IsActive);
        Assert.True(history[1].IsActive);
        Assert.NotEqual(history[0].Id, history[1].Id);

        var linkEntity = _db.Model.FindEntityType(typeof(HrEmployeeUserLink))!;
        Assert.DoesNotContain(linkEntity.GetForeignKeys(),
            key => key.PrincipalEntityType.ClrType == typeof(User));
        Assert.Equal("is_active = true",
            linkEntity.GetIndexes().Single(index => index.IsUnique
                && index.Properties.Any(property => property.Name == nameof(HrEmployeeUserLink.IdentityUserId)))
                .GetFilter());
    }

    [Fact]
    public async Task LinkRejectsDuplicateEmployeeUserAndCrossBusinessReferences()
    {
        var handler = LinkHandler();
        await handler.Handle(
            new LinkHrEmployeeUserCommand(_employeeId, 101, "Initial link."),
            CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new LinkHrEmployeeUserCommand(_employeeId, 102, "Duplicate employee."),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new LinkHrEmployeeUserCommand(_otherEmployeeId, 101, "Duplicate user."),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new LinkHrEmployeeUserCommand(_employeeId, 201, "Cross Business."),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new LinkHrEmployeeUserCommand(_employeeId, 999, "Missing user."),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new LinkHrEmployeeUserCommand(_employeeId, 202, "Inactive user."),
            CancellationToken.None));
        Assert.Null(await handler.Handle(
            new LinkHrEmployeeUserCommand(_crossBusinessEmployeeId, 102, "Cross-Business employee."),
            CancellationToken.None));
        Assert.Single(await _db.HrEmployeeUserLinks.ToArrayAsync());
    }

    [Fact]
    public async Task UnlinkRejectsStaleVersionAndInactiveHistoryCannotBeRewritten()
    {
        var linked = await LinkHandler().Handle(
            new LinkHrEmployeeUserCommand(_employeeId, 101, "Initial link."),
            CancellationToken.None);
        var unlinkHandler = new UnlinkHrEmployeeUserCommandHandler(
            Access(), new HrEmployeeStore(_db), _auditTransaction);
        await Assert.ThrowsAsync<HrEmployeeConcurrencyException>(() => unlinkHandler.Handle(
            new UnlinkHrEmployeeUserCommand(_employeeId, linked!.Version + 1, "Stale request."),
            CancellationToken.None));
        var unlinked = await unlinkHandler.Handle(
            new UnlinkHrEmployeeUserCommand(_employeeId, linked!.Version, "Valid unlink."),
            CancellationToken.None);
        var historical = await _db.HrEmployeeUserLinks.SingleAsync(row => row.Id == linked.Id);
        historical.LinkReason = "Changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
        Assert.False(unlinked!.IsActive);
    }

    [Fact]
    public async Task AuditFailureRollsBackLinkWriteAndDoesNotMutateLoginUser()
    {
        var before = await _db.Users.AsNoTracking().SingleAsync(user => user.Id == 101);
        _failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => LinkHandler().Handle(
            new LinkHrEmployeeUserCommand(_employeeId, 101, "Audit must be atomic."),
            CancellationToken.None));
        _db.ChangeTracker.Clear();
        Assert.Empty(await _db.HrEmployeeUserLinks.ToArrayAsync());
        _failure.Fail = false;
        var linked = await LinkHandler().Handle(
            new LinkHrEmployeeUserCommand(_employeeId, 101, "Link before unlink rollback."),
            CancellationToken.None);
        _failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => new UnlinkHrEmployeeUserCommandHandler(
                Access(), new HrEmployeeStore(_db), _auditTransaction)
            .Handle(new UnlinkHrEmployeeUserCommand(
                _employeeId, linked!.Version, "Unlink audit must be atomic."),
                CancellationToken.None));
        _db.ChangeTracker.Clear();
        Assert.True(await _db.HrEmployeeUserLinks.AsNoTracking()
            .Where(link => link.EmployeeId == _employeeId).Select(link => link.IsActive).SingleAsync());
        var after = await _db.Users.AsNoTracking().SingleAsync(user => user.Id == 101);
        Assert.Equal(before.RoleId, after.RoleId);
        Assert.Equal(before.PermissionOverrides, after.PermissionOverrides);
        Assert.True(after.IsActive);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private LinkHrEmployeeUserCommandHandler LinkHandler() =>
        new(Access(), new HrEmployeeStore(_db), new IdentityUserDirectory(_db), _auditTransaction);

    private static IHrEmployeeAccess Access()
    {
        var access = new Mock<IHrEmployeeAccess>();
        access.Setup(item => item.RequireBusinessId()).Returns(1);
        access.Setup(item => item.RequireRealUserId()).Returns(101);
        access.Setup(item => item.EnsureAsync(1, It.IsAny<HrEmployeeOperation>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return access.Object;
    }

    private static User User(long id, long businessId, string name, bool active = true) => new()
    {
        Id = id,
        BusinessId = businessId,
        Name = name,
        Email = $"{id}@example.invalid",
        PasswordHash = "test-hash",
        IsActive = active
    };

    private static HrEmployee Employee(long businessId, long employmentTypeId, string code) => new()
    {
        BusinessId = businessId,
        EmployeeCode = code,
        FirstName = "Test",
        DisplayName = code,
        CurrentEmploymentStatus = "Active",
        CurrentEmploymentTypeId = employmentTypeId,
        JoiningDate = new DateOnly(2026, 10, 1),
        Version = 1
    };

    private sealed class FailAuditInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context?.ChangeTracker.Entries<AuditEvent>()
                    .Any(entry => entry.State == EntityState.Added) == true)
                throw new IOException("Injected employee link Audit v2 persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
