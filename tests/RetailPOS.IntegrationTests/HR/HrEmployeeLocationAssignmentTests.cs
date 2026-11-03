using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Moq;
using RetailPOS.API.Services.HR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Employees;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrEmployeeLocationAssignmentTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private RetailPOSDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connection)
            .Options);
        await _db.Database.EnsureCreatedAsync();
        _db.Businesses.AddRange(
            new Business { Id = 1, Name = "Employee Location Business" },
            new Business { Id = 2, Name = "Other Employee Location Business" });
        _db.HrEmploymentTypes.Add(new HrEmploymentType
        {
            Id = 11, BusinessId = 1, Code = "Permanent", Name = "Permanent", IsActive = true
        });
        _db.HrEmployees.Add(new HrEmployee
        {
            Id = 101,
            BusinessId = 1,
            EmployeeCode = "EMP-LOC-1",
            FirstName = "Location",
            DisplayName = "Location Employee",
            CurrentEmploymentStatus = "Active",
            CurrentEmploymentTypeId = 11,
            JoiningDate = new DateOnly(2026, 1, 1),
            Version = 1
        });
        _db.HrEmploymentPeriods.Add(new HrEmploymentPeriod
        {
            Id = 201,
            BusinessId = 1,
            EmployeeId = 101,
            EmploymentTypeId = 11,
            StartDate = new DateOnly(2026, 1, 1),
            Status = "Active",
            SourceEventType = "Joined"
        });
        _db.OperationalLocationProjections.AddRange(Location(301, 1, "Outlet", "10", "First"),
            Location(302, 1, "Warehouse", "20", "Second"),
            Location(303, 2, "Outlet", "30", "Foreign"));
        await _db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReusablePositionUsesEmployeeLocationScopeAndAssignmentDatesNotLegacyPositionScope(bool legacy)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.HrEmployees.Add(new HrEmployee
        {
            Id = 102, BusinessId = 1, EmployeeCode = "EMP-LOC-2", FirstName = "Second",
            DisplayName = "Second Employee", CurrentEmploymentStatus = "Active",
            CurrentEmploymentTypeId = 11, JoiningDate = new(2026, 1, 1)
        });
        _db.HrEmploymentPeriods.Add(new HrEmploymentPeriod
        {
            BusinessId = 1, EmployeeId = 102, EmploymentTypeId = 11,
            StartDate = new(2026, 1, 1), Status = "Active", SourceEventType = "Joined"
        });
        _db.HrOrgUnits.Add(new HrOrgUnit
        {
            Id = 401, BusinessId = 1, Code = "SALES", Name = "Sales",
            UnitType = "Department", EffectiveFrom = new(2026, 1, 1)
        });
        _db.HrPositions.Add(new HrPosition
        {
            Id = 501, BusinessId = 1, Code = "EXEC", Name = "Sales Executive", OrgUnitId = 401,
            Status = HrPositionStatus.Vacant, OperationalLocationProjectionId = legacy ? 301 : null,
            EffectiveFrom = legacy ? today.AddYears(1) : null, EffectiveTo = legacy ? today.AddYears(2) : null
        });
        await _db.SaveChangesAsync();
        var locations = new HrEmployeeLocationAssignmentStore(_db);
        await locations.CreateAsync(1, 101, 101, 1, Assignment(301, "Primary", today), default);
        await locations.CreateAsync(1, 101, 102, 1, Assignment(302, "Primary", today), default);

        var organisation = new Mock<IHrOrganisationAccess>();
        organisation.Setup(access => access.RequireBusinessId()).Returns(1);
        organisation.Setup(access => access.RequireRealUserId()).Returns(101);
        organisation.Setup(access => access.GetLocationReadScopeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrOperationalLocationReadScope(1, [new("Outlet", "10")]));
        var access = new HrEmployeeAccessAdapter(organisation.Object, new HttpContextAccessor(), locations);
        var audit = new Mock<IRequiredAuditTransaction>();
        audit.Setup(transaction => transaction.ExecuteAsync(
                It.IsAny<RequiredAuditOperation>(),
                It.IsAny<Func<CancellationToken, Task<HrEmployeePositionAssignmentReadModel?>>>(),
                It.IsAny<Func<HrEmployeePositionAssignmentReadModel?, bool>>(),
                It.IsAny<CancellationToken>()))
            .Returns((RequiredAuditOperation _, Func<CancellationToken, Task<HrEmployeePositionAssignmentReadModel?>> action,
                Func<HrEmployeePositionAssignmentReadModel?, bool> _, CancellationToken token) => action(token));
        var employees = new HrEmployeeStore(_db);
        var handler = new AssignHrEmployeePositionCommandHandler(access, employees, audit.Object);
        var data = new HrEmployeePositionAssignmentData(501, 401, null, "Primary", today, null, "Reusable role");
        Assert.NotNull(await handler.Handle(new(101, 2, data), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new(102, 2, data), default));

        organisation.Setup(access => access.GetLocationReadScopeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HrOperationalLocationReadScope(1, [new("Warehouse", "20")]));
        Assert.NotNull(await handler.Handle(new(102, 2, data), default));
        Assert.Equal(2, await _db.HrEmployeePositionAssignments.CountAsync(row => row.PositionId == 501));
        organisation.Verify(access => access.EnsureAsync(1,
            HrOrganisationOperation.ManageEmployeePositionAssignments, null, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        organisation.Verify(access => access.EnsureAsync(It.IsAny<long>(),
            HrOrganisationOperation.ManageEmployeePositionAssignments, 301, It.IsAny<CancellationToken>()),
            Times.Never);
        await Assert.ThrowsAsync<ArgumentException>(() => employees.AssignPositionAsync(
            1, 101, 102, 3, data with { EffectiveFrom = new(2025, 12, 31) }, default));

        var imports = new HrOpeningImportStore(_db, employees, locations, TimeProvider.System);
        var row = new HrOpeningImportRow(1, "ROW", "EMP-LOC-2", null, 501, 401, null, "Primary", today, []);
        Assert.Empty(await imports.ValidateAsync(1, "Assignment", row, default));
        Assert.Contains(await imports.ValidateAsync(1, "Assignment", row with { LocationId = 303 }, default),
            error => error.Contains("not owned by this Business"));
        Assert.Contains(await imports.ValidateAsync(1, "Assignment",
            row with { EffectiveFrom = new(2025, 12, 31) }, default), error => error.Contains("precedes joining"));
        Assert.Contains(await imports.ValidateAsync(1, "Assignment",
            row with { EffectiveFrom = today.AddDays(1) }, default), error => error.Contains("future-effective"));
    }

    [Fact]
    public async Task PrimaryAndAdditionalAssignmentsPreserveSnapshotsAndCurrentPointer()
    {
        var store = new HrEmployeeLocationAssignmentStore(_db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var primary = await store.CreateAsync(1, 101, 101, 1,
            Assignment(301, "Primary", today), CancellationToken.None);
        var additional = await store.CreateAsync(1, 101, 101, 2,
            Assignment(302, "Additional", today), CancellationToken.None);

        Assert.NotNull(primary);
        Assert.NotNull(additional);
        Assert.Equal("First", primary.LocationName);
        Assert.Equal(301, await _db.HrEmployees.Where(row => row.Id == 101)
            .Select(row => row.CurrentPrimaryLocationProjectionId).SingleAsync());
        Assert.False(await store.IsEmployeeAccessibleAsync(1, 101,
            [new OperationalLocationSourceIdentity("Warehouse", "20")], CancellationToken.None));
        Assert.Empty((await new HrEmployeeStore(_db).SearchAsync(1, null, 1, 25,
            [new OperationalLocationSourceIdentity("Warehouse", "20")], CancellationToken.None)).Items);

        var source = await _db.OperationalLocationProjections.SingleAsync(row => row.Id == 301);
        source.Name = "Renamed source";
        await _db.SaveChangesAsync();
        var history = await store.GetHistoryAsync(1, 101, null, CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.Equal("First", history.Single(row => row.Id == primary!.Id).LocationName);
    }

    [Fact]
    public async Task OverlappingPrimaryAndCrossBusinessLocationAreRejected()
    {
        var store = new HrEmployeeLocationAssignmentStore(_db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await store.CreateAsync(1, 101, 101, 1,
            Assignment(301, "Primary", today), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(
            1, 101, 101, 2, Assignment(302, "Primary", today), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(
            1, 101, 101, 2, Assignment(303, "Additional", today), CancellationToken.None));
        Assert.Single(await _db.HrEmployeeLocationAssignments.ToListAsync());
    }

    [Fact]
    public async Task EmployeeLocationScopeMatchesOnlyAuthorizedLocationSources()
    {
        var store = new HrEmployeeLocationAssignmentStore(_db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await store.CreateAsync(1, 101, 101, 1,
            Assignment(301, "Primary", today), CancellationToken.None);

        Assert.True(await store.IsEmployeeAccessibleAsync(1, 101,
            [new OperationalLocationSourceIdentity("Outlet", "10")], CancellationToken.None));
        Assert.False(await store.IsEmployeeAccessibleAsync(1, 101,
            [new OperationalLocationSourceIdentity("Warehouse", "20")], CancellationToken.None));
        Assert.False(await store.IsEmployeeAccessibleAsync(2, 101, null, CancellationToken.None));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static OperationalLocationProjection Location(
        long id, long businessId, string type, string sourceId, string name) => new()
    {
        Id = id,
        BusinessId = businessId,
        SourceSystem = "POS",
        SourceEntityType = type,
        SourceEntityId = sourceId,
        Name = name,
        SourceVersion = 1,
        SourcePayloadHash = "hash-" + sourceId,
        SourceUpdatedAtUtc = DateTime.UtcNow,
        LastSyncedAtUtc = DateTime.UtcNow,
        IsSourceActive = true,
        SyncStatus = "Current"
    };

    private static HrEmployeeLocationAssignmentData Assignment(
        long locationId, string type, DateOnly effectiveFrom) =>
        new(locationId, type, false, effectiveFrom, null, "Test assignment");
}
