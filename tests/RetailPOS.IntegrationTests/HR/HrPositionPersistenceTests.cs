using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Organisation;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrPositionPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext _auditContext = new();
    private readonly FailAuditInterceptor _failure = new();
    private RetailPOSDbContext _db = null!;
    private CreateHrPositionCommandHandler _create = null!;
    private UpdateHrPositionCommandHandler _update = null!;
    private DeactivateHrPositionCommandHandler _deactivate = null!;
    private GetHrPositionsQueryHandler _query = null!;

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
            new Business { Id = 1, Name = "Position Test Business" },
            new Business { Id = 2, Name = "Other Business" });
        _db.HrOrgUnits.Add(new HrOrgUnit
        {
            Id = 11,
            BusinessId = 1,
            Code = "ORG-1",
            Name = "Operations",
            UnitType = "Department",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = 101,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedByUserId = 101
        });
        await _db.SaveChangesAsync();

        _auditContext.Initialize(new AuditContextSnapshot(
            Guid.NewGuid(), 101, 101, 5, 7, null, 1, AuditSource.API,
            "POST", "/api/hr/v1/positions", null, null, null, false, null, null, null, null,
            "position-creator", "HR"));
        var auditService = new AuditService(
            _db, _auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
        var auditTransaction = new RequiredAuditTransaction(
            _db, _auditContext, auditService, NullLogger<RequiredAuditTransaction>.Instance);
        var access = new TestAccess();
        var orgUnits = new Mock<IHrOrgUnitWriteStore>();
        orgUnits.Setup(store => store.GetLocationProjectionIdAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);
        var store = new HrPositionStore(_db);
        _create = new CreateHrPositionCommandHandler(access, orgUnits.Object, store, auditTransaction);
        _update = new UpdateHrPositionCommandHandler(access, orgUnits.Object, store, auditTransaction);
        _deactivate = new DeactivateHrPositionCommandHandler(access, store, auditTransaction);
        _query = new GetHrPositionsQueryHandler(access, store);
    }

    [Fact]
    public async Task NewPositionIsUndatedAndUnscopedEvenForLegacyRequests()
    {
        var created = await _create.Handle(
            new CreateHrPositionCommand(Data("SALES", "Sales Executive") with
            {
                OperationalLocationProjectionId = 999,
                EffectiveFrom = new DateOnly(2027, 1, 1),
                EffectiveTo = new DateOnly(2027, 12, 31)
            }), CancellationToken.None);
        Assert.Null(created.OperationalLocationProjectionId);
        Assert.Null(created.EffectiveFrom);
        Assert.Null(created.EffectiveTo);
        var row = await _db.HrPositions.SingleAsync();
        Assert.Null(row.OperationalLocationProjectionId);
        Assert.Null(row.EffectiveFrom);
    }

    [Fact]
    public async Task LegacyValuesRemainReadableAndUnchangedOnSimplifiedEdit()
    {
        var location = new OperationalLocationProjection
        {
            Id = 301, BusinessId = 1, SourceSystem = "POS", SourceEntityType = "Outlet",
            SourceEntityId = "10", Name = "Legacy outlet", SourcePayloadHash = "legacy",
            SyncStatus = "Current", IsSourceActive = true, SourceVersion = 1
        };
        _db.OperationalLocationProjections.Add(location);
        var legacy = new HrPosition
        {
            BusinessId = 1, Code = "LEGACY", Name = "Legacy role", OrgUnitId = 11,
            OperationalLocationProjectionId = 301, Status = HrPositionStatus.Vacant,
            EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 12, 31)
        };
        _db.HrPositions.Add(legacy);
        await _db.SaveChangesAsync();

        var store = new HrPositionStore(_db);
        // Legacy Position location no longer hides a reusable role in an unscoped department.
        var visible = await store.GetPositionsAsync(1, null, new(1, []), CancellationToken.None);
        Assert.Equal(301, Assert.Single(visible).OperationalLocationProjectionId);
        Assert.Null(await store.GetPositionLocationProjectionIdAsync(1, legacy.Id, CancellationToken.None));
        var updated = await _update.Handle(new UpdateHrPositionCommand(legacy.Id, legacy.Version,
            new HrPositionWriteData("LEGACY", "Renamed reusable role", 11)), CancellationToken.None);
        Assert.Equal(301, updated!.OperationalLocationProjectionId);
        Assert.Equal(new DateOnly(2026, 1, 1), updated.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), updated.EffectiveTo);

        var department = await _db.HrOrgUnits.SingleAsync();
        department.OperationalLocationProjectionId = 301;
        await _db.SaveChangesAsync();
        Assert.Empty(await store.GetPositionsAsync(1, null, new(1, []), CancellationToken.None));
        Assert.Single(await store.GetPositionsAsync(1, null,
            new(1, [new("Outlet", "10")]), CancellationToken.None));
    }

    [Fact]
    public async Task PositionChangesCommitWithAuditAndReadQueriesStayBusinessScoped()
    {
        var position = await _create.Handle(
            new CreateHrPositionCommand(Data("POS-1", "Operations Lead")), CancellationToken.None);

        Assert.Equal(1, position.BusinessId);
        Assert.Equal(1, await _db.AuditEvents.CountAsync());

        var visible = await _query.Handle(new GetHrPositionsQuery(), CancellationToken.None);
        Assert.Single(visible);
        Assert.Empty(await new HrPositionStore(_db).GetPositionsAsync(
            2, null, new HrOperationalLocationReadScope(2, null), CancellationToken.None));
    }

    [Fact]
    public async Task ReportingCyclesAndStaleVersionsAreRejected()
    {
        var parent = await _create.Handle(
            new CreateHrPositionCommand(Data("POS-1", "Operations Lead")), CancellationToken.None);
        var child = await _create.Handle(
            new CreateHrPositionCommand(Data("POS-2", "Shift Lead", reportsTo: parent.Id)),
            CancellationToken.None);
        Assert.Equal(parent.Id, child.ReportsToPositionId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _update.Handle(
            new UpdateHrPositionCommand(parent.Id, parent.Version,
                Data("POS-1", "Operations Lead", reportsTo: parent.Id)), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _update.Handle(
            new UpdateHrPositionCommand(parent.Id, parent.Version,
                Data("POS-1", "Operations Lead", reportsTo: child.Id)), CancellationToken.None));
        await Assert.ThrowsAsync<HrOrganisationConcurrencyException>(() => _update.Handle(
            new UpdateHrPositionCommand(child.Id, child.Version + 1,
                Data("POS-2", "Shift Lead", reportsTo: parent.Id)), CancellationToken.None));

        Assert.Equal(2, await _db.HrPositions.CountAsync());
        Assert.Equal(2, await _db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task ConcurrentStaleWriteIsRejectedByDatabaseVersionToken()
    {
        var position = await _create.Handle(
            new CreateHrPositionCommand(Data("POS-1", "Operations Lead")), CancellationToken.None);
        await using var staleDb = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connection)
            .Options);
        var stalePosition = await staleDb.HrPositions.SingleAsync(row => row.Id == position.Id);

        await _update.Handle(
            new UpdateHrPositionCommand(position.Id, position.Version,
                Data("POS-1", "Updated Operations Lead")), CancellationToken.None);

        stalePosition.Name = "Stale Write";
        stalePosition.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());

        Assert.Equal("Updated Operations Lead",
            await _db.HrPositions.Where(row => row.Id == position.Id).Select(row => row.Name).SingleAsync());
    }

    [Fact]
    public async Task DeactivationRetainsPositionAndAuditFailureRollsBackCreate()
    {
        var position = await _create.Handle(
            new CreateHrPositionCommand(Data("POS-1", "Operations Lead")), CancellationToken.None);
        var inactive = await _deactivate.Handle(
            new DeactivateHrPositionCommand(position.Id, position.Version, new DateOnly(2026, 12, 31)),
            CancellationToken.None);

        Assert.Equal("Inactive", inactive!.Status);
        Assert.Equal(2, await _db.AuditEvents.CountAsync());
        Assert.Single(await _db.HrPositions.ToListAsync());

        _failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => _create.Handle(
            new CreateHrPositionCommand(Data("POS-2", "Failed Create")), CancellationToken.None));

        Assert.Single(await _db.HrPositions.ToListAsync());
        Assert.Equal(2, await _db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task ForeignBusinessOrgUnitAndDuplicateCodeAreRejected()
    {
        await _create.Handle(
            new CreateHrPositionCommand(Data("POS-1", "Operations Lead")), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new HrPositionStore(_db).CreateAsync(
            2, Data("POS-2", "Foreign Reference"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _create.Handle(
            new CreateHrPositionCommand(Data("POS-1", "Duplicate")), CancellationToken.None));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static HrPositionWriteData Data(
        string code,
        string name,
        long? reportsTo = null) =>
        new(code, name, 11, ReportsToPositionId: reportsTo);

    private sealed class TestAccess : IHrOrganisationAccess
    {
        public long RequireBusinessId() => 1;
        public long RequireRealUserId() => 101;

        public Task EnsureAsync(
            long businessId,
            HrOrganisationOperation operation,
            long? locationProjectionId,
            CancellationToken cancellationToken) =>
            businessId == 1 ? Task.CompletedTask : throw new UnauthorizedAccessException();

        public Task<HrOperationalLocationReadScope> GetLocationReadScopeAsync(
            long businessId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HrOperationalLocationReadScope(businessId, null));

        public Task EnsureLocationAccessibleAsync(
            long businessId,
            long locationProjectionId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

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
                throw new IOException("Injected Position Audit v2 persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
