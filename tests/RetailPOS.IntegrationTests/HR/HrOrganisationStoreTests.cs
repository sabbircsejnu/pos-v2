using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Organisation;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrOrganisationStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private RetailPOSDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _db.Businesses.AddRange(new Business { Id = 1, Name = "Organisation tests" },
            new Business { Id = 2, Name = "Other Business" });
        _db.OperationalLocationProjections.AddRange(
            Location(101, 1, "Outlet"), Location(102, 1, "Warehouse"), Location(201, 2, "Outlet"));
        await _db.SaveChangesAsync();
        _db.HrOrgUnits.AddRange(Unit(1, 1, "GLOBAL"), Unit(2, 1, "OUTLET", 101),
            Unit(3, 1, "WAREHOUSE", 102), Unit(4, 2, "FOREIGN", 201));
        await _db.SaveChangesAsync();
        _db.HrOrgUnits.AddRange(Unit(5, 1, "OUTLET-CHILD", parentId: 2),
            Unit(6, 1, "WAREHOUSE-CHILD", parentId: 3));
        await _db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Outlet", new[] { "GLOBAL", "OUTLET", "OUTLET-CHILD" })]
    [InlineData("Warehouse", new[] { "GLOBAL", "WAREHOUSE", "WAREHOUSE-CHILD" })]
    public async Task ScopedOrganisationListPreservesSourceTypeAndParentVisibility(
        string sourceType, string[] expectedCodes)
    {
        var store = new HrOrganisationStore(_db);
        // Matching source IDs must not confer access to the other source type or Business.
        var scope = new HrOperationalLocationReadScope(1, [new(sourceType, "10")]);
        var rows = await store.GetOrgUnitsAsync(1, null, scope, default);

        Assert.Equal(expectedCodes, rows.Select(row => row.Code));
        Assert.All(rows, row => Assert.Equal(1, row.BusinessId));
    }

    [Fact]
    public async Task EmptyLocationScopeReturnsOnlyBusinessWideUnitsWithoutExposingScopedChildren()
    {
        var rows = await new HrOrganisationStore(_db).GetOrgUnitsAsync(1, null, new(1, []), default);
        Assert.Equal("GLOBAL", Assert.Single(rows).Code);
    }

    [Fact]
    public async Task BusinessWideAndCombinedScopesPreserveSearchAndOrdering()
    {
        var store = new HrOrganisationStore(_db);
        var all = await store.GetOrgUnitsAsync(1, null, new(1, null), default);
        Assert.Equal(new[] { "GLOBAL", "OUTLET", "OUTLET-CHILD", "WAREHOUSE", "WAREHOUSE-CHILD" },
            all.Select(row => row.Code));

        var combined = new HrOperationalLocationReadScope(1,
            [new("Outlet", "10"), new("Warehouse", "10")]);
        Assert.Equal(all.Select(row => row.Id),
            (await store.GetOrgUnitsAsync(1, null, combined, default)).Select(row => row.Id));
        var search = await store.GetOrgUnitsAsync(1, "child", combined, default);
        Assert.Equal(new[] { "OUTLET-CHILD", "WAREHOUSE-CHILD" }, search.Select(row => row.Code));
    }

    [Fact]
    public async Task CreationUsesAuthoritativeUtcDateWhenDatesAreOmitted()
    {
        var now = new DateTimeOffset(2026, 10, 23, 0, 15, 0, TimeSpan.Zero);
        var store = new HrOrganisationStore(_db, new FixedClock(now));
        var created = await store.CreateAsync(1, 101,
            new("SALES", "Sales", "Department"), default);

        Assert.Equal(new DateOnly(2026, 10, 23), created.EffectiveFrom);
        Assert.Equal(now.UtcDateTime, created.CreatedAtUtc);
        Assert.Null(created.EffectiveTo);
        Assert.Null(created.ParentOrgUnitId);
    }

    [Fact]
    public async Task OrdinaryEditPreservesHistoricalStartEndAndLocationAndSupportsStatus()
    {
        var store = new HrOrganisationStore(_db);
        var row = await _db.HrOrgUnits.SingleAsync(unit => unit.Id == 2);
        row.EffectiveTo = new(2027, 1, 1);
        await _db.SaveChangesAsync();
        var updated = await store.UpdateAsync(1, 101, row.Id, row.Version,
            new("OUTLET", "Renamed department", "Department",
                OperationalLocationProjectionId: row.OperationalLocationProjectionId,
                EffectiveTo: row.EffectiveTo, IsActive: false), default);

        Assert.NotNull(updated);
        Assert.Equal(new DateOnly(2026, 1, 1), updated.EffectiveFrom);
        Assert.Equal(new DateOnly(2027, 1, 1), updated.EffectiveTo);
        Assert.Equal(101, updated.OperationalLocationProjectionId);
        Assert.False(updated.IsActive);
        Assert.Equal(2, updated.Version);
        var reactivated = await store.UpdateAsync(1, 101, row.Id, updated.Version,
            new("OUTLET", "Renamed department", "Department",
                OperationalLocationProjectionId: 101, EffectiveTo: updated.EffectiveTo), default);
        Assert.True(reactivated!.IsActive);
    }

    [Fact]
    public async Task ParentMustBeActiveInSameBusinessAndHierarchyCannotCycle()
    {
        var store = new HrOrganisationStore(_db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(1, 101,
            new("BAD", "Foreign parent", "Team", ParentOrgUnitId: 4), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(1, 101,
            new("BAD", "Missing parent", "Team", ParentOrgUnitId: 999), default));
        var inactive = await _db.HrOrgUnits.SingleAsync(unit => unit.Id == 3);
        inactive.IsActive = false;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(1, 101,
            new("BAD", "Inactive parent", "Team", ParentOrgUnitId: 3), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(1, 101, 1, 1,
            new("GLOBAL", "Self parent", "Department", ParentOrgUnitId: 1), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(1, 101, 2, 1,
            new("OUTLET", "Cycle", "Department", ParentOrgUnitId: 5), default));
    }

    [Fact]
    public async Task UpdatesAreBusinessScopedAndVersionedAndCodesRemainUniquePerBusiness()
    {
        var store = new HrOrganisationStore(_db);
        Assert.Null(await store.UpdateAsync(1, 101, 4, 1, new("X", "Foreign", "Department"), default));
        await Assert.ThrowsAsync<HrOrganisationConcurrencyException>(() => store.UpdateAsync(1, 101,
            1, 99, new("GLOBAL", "Stale", "Department"), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(1, 101,
            new("GLOBAL", "Duplicate", "Department"), default));
        var allowed = await store.CreateAsync(2, 201, new("GLOBAL", "Other Business", "Department"), default);
        Assert.Equal(2, allowed.BusinessId);
        Assert.Equal("FOREIGN", (await _db.HrOrgUnits.SingleAsync(unit => unit.Id == 4)).Code);
    }

    [Fact]
    public async Task LegacyDatesRemainSupportedAndResolvedDatesAreValidated()
    {
        var store = new HrOrganisationStore(_db, new FixedClock(
            new DateTimeOffset(2026, 10, 23, 0, 15, 0, TimeSpan.Zero)));
        var created = await store.CreateAsync(1, 101,
            new("LEGACY", "Legacy", "Department", EffectiveFrom: new(2026, 1, 1),
                EffectiveTo: new(2026, 12, 31)), default);
        Assert.Equal(new DateOnly(2026, 1, 1), created.EffectiveFrom);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(1, 101,
            new("BAD", "Bad dates", "Department", EffectiveTo: new(2026, 10, 22)), default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateAsync(1, 101, created.Id, 1,
            new("LEGACY", "Bad edit", "Department", EffectiveTo: new(2025, 12, 31)), default));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static OperationalLocationProjection Location(long id, long businessId, string type) => new()
    {
        Id = id, BusinessId = businessId, SourceSystem = "POS", SourceEntityType = type,
        SourceEntityId = "10", Name = type, SourceVersion = 1, SourcePayloadHash = "test",
        IsSourceActive = true, SyncStatus = "Current"
    };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static HrOrgUnit Unit(long id, long businessId, string code,
        long? locationId = null, long? parentId = null) => new()
    {
        Id = id, BusinessId = businessId, Code = code, Name = code, UnitType = "Department",
        OperationalLocationProjectionId = locationId, ParentOrgUnitId = parentId,
        EffectiveFrom = new(2026, 1, 1), IsActive = true,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        CreatedByUserId = 1, UpdatedByUserId = 1
    };
}
