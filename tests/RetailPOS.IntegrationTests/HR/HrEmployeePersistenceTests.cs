using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Employees;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrEmployeePersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext _auditContext = new();
    private readonly FailAuditInterceptor _failure = new();
    private RetailPOSDbContext _db = null!;
    private TestAccess _access = null!;
    private CreateHrEmployeeCommandHandler _create = null!;
    private UpdateHrEmployeeCommandHandler _update = null!;
    private GetHrEmployeeProfileQueryHandler _profile = null!;
    private SearchHrEmployeesQueryHandler _search = null!;
    private RequiredAuditTransaction _auditTransaction = null!;

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
            new Business { Id = 1, Name = "Employee Test Business" },
            new Business { Id = 2, Name = "Other Employee Business" });
        _db.HrEmploymentTypes.AddRange(
            new HrEmploymentType { Id = 11, BusinessId = 1, Code = "Permanent", Name = "Permanent" },
            new HrEmploymentType { Id = 12, BusinessId = 1, Code = "Probation", Name = "Probation" },
            new HrEmploymentType { Id = 13, BusinessId = 1, Code = "Disabled", Name = "Disabled", IsActive = false },
            new HrEmploymentType { Id = 21, BusinessId = 2, Code = "Permanent", Name = "Permanent" });
        await _db.SaveChangesAsync();

        _auditContext.Initialize(new AuditContextSnapshot(
            Guid.NewGuid(), 101, 101, 5, 7, null, 1, AuditSource.API,
            "POST", "/api/hr/v1/employees", null, null, null, false, null, null, null, null,
            "employee-creator", "HR"));
        var auditService = new AuditService(
            _db, _auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
        _auditTransaction = new RequiredAuditTransaction(
            _db, _auditContext, auditService, NullLogger<RequiredAuditTransaction>.Instance);
        _access = new TestAccess();
        var store = new HrEmployeeStore(_db);
        _create = new CreateHrEmployeeCommandHandler(_access, store, _auditTransaction);
        _update = new UpdateHrEmployeeCommandHandler(_access, store, _auditTransaction);
        _profile = new GetHrEmployeeProfileQueryHandler(_access, store, _auditTransaction);
        _search = new SearchHrEmployeesQueryHandler(_access, store);
    }

    public sealed class HrEmployeePostgresConcurrencyTests
    {
        [Fact]
        public async Task PostgreSqlSerializesConcurrentLifecycleTransitions()
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            var database = await PostgresTestDatabase.CreateAsync(
                GetPostgresConnectionString(), PostgresTestDatabasePrefix.MigrationBaseline);
            try
            {
                var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                    .UseNpgsql(database.ConnectionString)
                    .Options;
                long employeeId;
                await using (var setup = new RetailPOSDbContext(options))
                {
                    await setup.Database.MigrateAsync();
                    var timestamp = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
                    setup.Businesses.Add(new Business
                    {
                        Id = 98202,
                        Name = "Employee Lifecycle Concurrency Test",
                        CreatedAt = timestamp,
                        UpdatedAt = timestamp
                    });
                    await setup.SaveChangesAsync();
                    var type = new HrEmploymentType
                    {
                        BusinessId = 98202,
                        Code = "Permanent",
                        Name = "Permanent",
                        IsActive = true
                    };
                    setup.HrEmploymentTypes.Add(type);
                    await setup.SaveChangesAsync();
                    var employee = new HrEmployee
                    {
                        BusinessId = 98202,
                        EmployeeCode = "EMP-LIFECYCLE-PG",
                        FirstName = "Alex",
                        DisplayName = "Alex Employee",
                        CurrentEmploymentStatus = "Active",
                        CurrentEmploymentTypeId = type.Id,
                        JoiningDate = new DateOnly(2026, 10, 1),
                        Version = 1
                    };
                    setup.HrEmployees.Add(employee);
                    await setup.SaveChangesAsync();
                    employeeId = employee.Id;
                    setup.HrEmploymentPeriods.Add(new HrEmploymentPeriod
                    {
                        BusinessId = 98202,
                        EmployeeId = employeeId,
                        EmploymentTypeId = type.Id,
                        StartDate = employee.JoiningDate,
                        Status = "Active",
                        SourceEventType = "Joined"
                    });
                    await setup.SaveChangesAsync();
                }

                async Task<bool> TryTransitionAsync()
                {
                    var auditContext = new AuditContext();
                    auditContext.Initialize(new AuditContextSnapshot(
                        Guid.NewGuid(), 101, 101, 5, 7, null, 98202, AuditSource.API,
                        "POST", "/api/hr/v1/employees/lifecycle", null, null, null, false,
                        null, null, null, null, "employee-concurrency", "HR"));
                    await using var db = new RetailPOSDbContext(
                        new DbContextOptionsBuilder<RetailPOSDbContext>()
                            .UseNpgsql(database.ConnectionString)
                            .AddInterceptors(new AuditChangeTrackerInterceptor(
                                auditContext, NullLogger<AuditChangeTrackerInterceptor>.Instance))
                            .Options);
                    var auditService = new AuditService(
                        db, auditContext, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
                    var auditTransaction = new RequiredAuditTransaction(
                        db, auditContext, auditService, NullLogger<RequiredAuditTransaction>.Instance);
                    var handler = new TransitionHrEmployeeLifecycleCommandHandler(
                        new PostgresTestAccess(), new HrEmployeeStore(db), auditTransaction);
                    try
                    {
                        await handler.Handle(new TransitionHrEmployeeLifecycleCommand(
                            employeeId, 1,
                            new HrEmployeeLifecycleTransitionData(
                                "Probation", DateOnly.FromDateTime(DateTime.UtcNow), "Concurrent update")),
                            CancellationToken.None);
                        return true;
                    }
                    catch (HrEmployeeConcurrencyException)
                    {
                        return false;
                    }
                }

                var outcomes = await Task.WhenAll(TryTransitionAsync(), TryTransitionAsync());
                Assert.Single(outcomes, succeeded => succeeded);
                Assert.Single(outcomes, succeeded => !succeeded);
                await using var verification = new RetailPOSDbContext(options);
                Assert.Equal("Probation", (await verification.HrEmployees.SingleAsync(row => row.Id == employeeId))
                    .CurrentEmploymentStatus);
                Assert.Equal(1, await verification.HrEmployeeLifecycleEvents.CountAsync(row =>
                    row.EmployeeId == employeeId && row.EventType == "Probation"));
            }
            finally
            {
                await database.DisposeAsync();
            }
        }

        [Fact]
        public async Task PostgreSqlRejectsConcurrentStaleEmployeeVersion()
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            var database = await PostgresTestDatabase.CreateAsync(
                GetPostgresConnectionString(), PostgresTestDatabasePrefix.MigrationBaseline);
            try
            {
                var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                    .UseNpgsql(database.ConnectionString)
                    .Options;
                await using (var setup = new RetailPOSDbContext(options))
                {
                    await setup.Database.MigrateAsync();
                    var businessTimestamp = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
                    setup.Businesses.Add(new Business
                    {
                        Id = 98201,
                        Name = "Employee Concurrency Test",
                        CreatedAt = businessTimestamp,
                        UpdatedAt = businessTimestamp
                    });
                    await setup.SaveChangesAsync();
                    var type = new HrEmploymentType
                    {
                        BusinessId = 98201,
                        Code = "Permanent",
                        Name = "Permanent",
                        IsActive = true
                    };
                    setup.HrEmploymentTypes.Add(type);
                    await setup.SaveChangesAsync();
                    setup.HrEmployees.Add(new HrEmployee
                    {
                        BusinessId = 98201,
                        EmployeeCode = "EMP-PG-1",
                        FirstName = "Alex",
                        DisplayName = "Alex Employee",
                        CurrentEmploymentStatus = "Active",
                        CurrentEmploymentTypeId = type.Id,
                        JoiningDate = new DateOnly(2026, 10, 1),
                        Version = 1
                    });
                    await setup.SaveChangesAsync();
                }

                await using var writer = new RetailPOSDbContext(options);
                await using var staleWriter = new RetailPOSDbContext(options);
                var current = await writer.HrEmployees.SingleAsync(row => row.BusinessId == 98201);
                var stale = await staleWriter.HrEmployees.SingleAsync(row => row.BusinessId == 98201);
                current.DisplayName = "Updated Employee";
                current.Version++;
                await writer.SaveChangesAsync();

                stale.DisplayName = "Stale Employee";
                stale.Version++;
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleWriter.SaveChangesAsync());
                Assert.Equal("Updated Employee",
                    await writer.HrEmployees.Where(row => row.BusinessId == 98201)
                        .Select(row => row.DisplayName).SingleAsync());
            }
            finally
            {
                await database.DisposeAsync();
            }
        }

        private static string GetPostgresConnectionString()
        {
            var configuredConnection = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
            if (!string.IsNullOrWhiteSpace(configuredConnection))
                return configuredConnection;

            var settingsPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "RetailPOS.API", "appsettings.json"));
            return new ConfigurationBuilder()
                .AddJsonFile(settingsPath, optional: false)
                .Build()
                .GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("PostgreSQL test connection is not configured.");
        }

        private sealed class PostgresTestAccess : IHrEmployeeAccess
        {
            public long RequireBusinessId() => 98202;
            public long RequireRealUserId() => 101;
            public HrEmployeeFieldAccess GetFieldAccess() => new(false, false);
            public Task EnsureAsync(
                long businessId, HrEmployeeOperation operation, CancellationToken cancellationToken) =>
                businessId == 98202
                    ? Task.CompletedTask
                    : throw new UnauthorizedAccessException("Business access denied.");
            public Task EnsurePositionAssignmentAsync(
                long businessId, long? locationProjectionId, CancellationToken cancellationToken) =>
                businessId == 98202
                    ? Task.CompletedTask
                    : throw new UnauthorizedAccessException("Position access denied.");
        }
    }

    [Fact]
    public async Task CreateCommitsEmployeeInitialPeriodAndAuditTogether()
    {
        var profile = await _create.Handle(new CreateHrEmployeeCommand(Data()), CancellationToken.None);

        Assert.True(profile.Id > 0);
        Assert.Null(profile.SensitiveFields.NationalId);
        Assert.Equal(1, await _db.HrEmployees.CountAsync());
        var period = await _db.HrEmploymentPeriods.SingleAsync();
        Assert.Equal(profile.Id, period.EmployeeId);
        Assert.Equal(1, period.BusinessId);
        Assert.Equal(new DateOnly(2026, 10, 1), period.StartDate);
        Assert.Equal("Active", period.Status);
        Assert.Equal("Joined", period.SourceEventType);
        Assert.Single(await _db.AuditEvents.ToListAsync());
        var serializedAudit = JsonSerializer.Serialize(await _db.AuditEvents.AsNoTracking().ToArrayAsync());
        Assert.DoesNotContain("NID-123", serializedAudit, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0101", serializedAudit, StringComparison.Ordinal);
        Assert.DoesNotContain("Present address", serializedAudit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateCodeIsRejectedWithinBusinessAndAllowedAcrossBusinesses()
    {
        await _create.Handle(new CreateHrEmployeeCommand(Data()), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _create.Handle(
            new CreateHrEmployeeCommand(Data() with { EmployeeCode = " emp-1 " }),
            CancellationToken.None));

        _access.BusinessId = 2;
        SetAuditBusiness(2);
        var otherBusiness = await _create.Handle(
            new CreateHrEmployeeCommand(Data() with { EmploymentTypeId = 21 }), CancellationToken.None);
        Assert.Equal("EMP-1", otherBusiness.EmployeeCode);
        Assert.Equal(2, await _db.HrEmployees.CountAsync());
        Assert.Equal(2, await _db.HrEmploymentPeriods.CountAsync());
    }

    [Fact]
    public async Task CreateRejectsInactiveOrForeignBusinessEmploymentTypes()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _create.Handle(
            new CreateHrEmployeeCommand(Data() with { EmploymentTypeId = 13 }), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _create.Handle(
            new CreateHrEmployeeCommand(Data() with { EmploymentTypeId = 21 }), CancellationToken.None));

        Assert.Empty(await _db.HrEmployees.ToListAsync());
        Assert.Empty(await _db.HrEmploymentPeriods.ToListAsync());
    }

    [Fact]
    public async Task BusinessScopeAndSensitiveFieldsAreEnforcedAcrossSearchAndProfile()
    {
        var created = await _create.Handle(new CreateHrEmployeeCommand(Data()), CancellationToken.None);
        var results = await _search.Handle(new SearchHrEmployeesQuery(null), CancellationToken.None);
        Assert.Single(results.Items);
        var serializedSearch = JsonSerializer.Serialize(results);
        Assert.DoesNotContain("NID-123", serializedSearch, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0101", serializedSearch, StringComparison.Ordinal);
        Assert.DoesNotContain("Present address", serializedSearch, StringComparison.Ordinal);

        _access.FieldAccess = new HrEmployeeFieldAccess(false, false);
        var restricted = await _profile.Handle(
            new GetHrEmployeeProfileQuery(created.Id), CancellationToken.None);
        Assert.Null(restricted!.SensitiveFields.Mobile);
        Assert.Null(restricted.SensitiveFields.NationalId);

        _access.FieldAccess = new HrEmployeeFieldAccess(true, false);
        var beforeReadAudit = await _db.AuditEvents.CountAsync();
        var sensitive = await _profile.Handle(
            new GetHrEmployeeProfileQuery(created.Id), CancellationToken.None);
        Assert.Equal("555-0101", sensitive!.SensitiveFields.Mobile);
        Assert.Null(sensitive.SensitiveFields.NationalId);
        Assert.Equal(beforeReadAudit + 1, await _db.AuditEvents.CountAsync());

        _access.BusinessId = 2;
        SetAuditBusiness(2);
        Assert.Null(await _profile.Handle(new GetHrEmployeeProfileQuery(created.Id), CancellationToken.None));
        Assert.Empty((await _search.Handle(new SearchHrEmployeesQuery(null), CancellationToken.None)).Items);
    }

    [Fact]
    public async Task AuditFailureRollsBackEmployeeAndInitialPeriodAndFailsClosedOnSensitiveRead()
    {
        _failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => _create.Handle(
            new CreateHrEmployeeCommand(Data()), CancellationToken.None));
        Assert.Empty(await _db.HrEmployees.ToListAsync());
        Assert.Empty(await _db.HrEmploymentPeriods.ToListAsync());
        Assert.Empty(await _db.AuditEvents.ToListAsync());

        _failure.Fail = false;
        var employee = await _create.Handle(new CreateHrEmployeeCommand(Data()), CancellationToken.None);
        _failure.Fail = true;
        _access.FieldAccess = new HrEmployeeFieldAccess(true, false);
        await Assert.ThrowsAsync<IOException>(() => _profile.Handle(
            new GetHrEmployeeProfileQuery(employee.Id), CancellationToken.None));
        var updateData = new HrEmployeeUpdateData(
            "Changed", null, "Employee", "Changed Employee",
            null, null, null, null, null, null, null, null, null, null, null);
        await Assert.ThrowsAsync<IOException>(() => _update.Handle(
            new UpdateHrEmployeeCommand(employee.Id, employee.Version, updateData), CancellationToken.None));
        _db.ChangeTracker.Clear();
        Assert.Single(await _db.HrEmployees.ToListAsync());
        var unchanged = await _db.HrEmployees.SingleAsync();
        Assert.Equal("Alex Employee", unchanged.DisplayName);
        Assert.Equal(employee.Version, unchanged.Version);
        Assert.Equal(1, await _db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task StaleVersionIsRejectedAndEmployeeDoesNotRequireIdentityRelationship()
    {
        var created = await _create.Handle(new CreateHrEmployeeCommand(Data()), CancellationToken.None);
        var updateData = new HrEmployeeUpdateData(
            "Alexandra", null, "Employee", "Alexandra Employee",
            null, null, null, null, null, null, null, null, null, null, null);
        var updated = await _update.Handle(
            new UpdateHrEmployeeCommand(created.Id, created.Version, updateData), CancellationToken.None);
        Assert.Equal("Alexandra Employee", updated!.DisplayName);

        await Assert.ThrowsAsync<HrEmployeeConcurrencyException>(() => _update.Handle(
            new UpdateHrEmployeeCommand(created.Id, created.Version, updateData), CancellationToken.None));

        var employeeEntity = _db.Model.FindEntityType(typeof(HrEmployee))!;
        Assert.DoesNotContain(employeeEntity.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(User));
        Assert.Empty(await _db.Users.ToListAsync());
        Assert.Equal(1, await _db.HrEmploymentPeriods.CountAsync());
    }

    [Fact]
    public async Task EmployeeCannotHaveOverlappingActiveEmploymentPeriods()
    {
        var created = await _create.Handle(new CreateHrEmployeeCommand(Data()), CancellationToken.None);
        _db.HrEmploymentPeriods.Add(new HrEmploymentPeriod
        {
            BusinessId = 1,
            EmployeeId = created.Id,
            EmploymentTypeId = 11,
            StartDate = new DateOnly(2027, 1, 1),
            Status = "Active",
            SourceEventType = "Joined"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());

        _db.ChangeTracker.Clear();
        Assert.Single(await _db.HrEmploymentPeriods.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task LifecycleTransitionsRejectDirectExitAndPreserveEmploymentPeriod()
    {
        var created = await _create.Handle(new CreateHrEmployeeCommand(Data()), CancellationToken.None);
        var store = new HrEmployeeStore(_db);
        var lifecycle = new TransitionHrEmployeeLifecycleCommandHandler(_access, store, _auditTransaction);
        var rehire = new RehireHrEmployeeCommandHandler(_access, store, _auditTransaction);
        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => rehire.Handle(
            new RehireHrEmployeeCommand(created.Id, created.Version, new HrEmployeeRehireData(
                11, "Active", effectiveDate, "Not exited")),
            CancellationToken.None));
        var typeChanged = await lifecycle.Handle(new TransitionHrEmployeeLifecycleCommand(
            created.Id, created.Version, new HrEmployeeLifecycleTransitionData(
                "EmploymentTypeChanged", effectiveDate, "Type changed", 12)), CancellationToken.None);
        var employee = await _db.HrEmployees.SingleAsync(row => row.Id == created.Id);
        var probation = await lifecycle.Handle(new TransitionHrEmployeeLifecycleCommand(
            created.Id, employee.Version, new HrEmployeeLifecycleTransitionData(
                "Probation", effectiveDate, "Start probation")), CancellationToken.None);
        employee = await _db.HrEmployees.SingleAsync(row => row.Id == created.Id);
        var confirmed = await lifecycle.Handle(new TransitionHrEmployeeLifecycleCommand(
            created.Id, employee.Version, new HrEmployeeLifecycleTransitionData(
                "Confirmed", effectiveDate, "Confirmed")), CancellationToken.None);
        employee = await _db.HrEmployees.SingleAsync(row => row.Id == created.Id);
        await lifecycle.Handle(new TransitionHrEmployeeLifecycleCommand(
            created.Id, employee.Version, new HrEmployeeLifecycleTransitionData(
                "Suspended", effectiveDate, "Suspended")), CancellationToken.None);
        employee = await _db.HrEmployees.SingleAsync(row => row.Id == created.Id);
        await lifecycle.Handle(new TransitionHrEmployeeLifecycleCommand(
            created.Id, employee.Version, new HrEmployeeLifecycleTransitionData(
                "Reinstated", effectiveDate, "Reinstated")), CancellationToken.None);
        employee = await _db.HrEmployees.SingleAsync(row => row.Id == created.Id);
        employee = await _db.HrEmployees.SingleAsync(row => row.Id == created.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => lifecycle.Handle(
            new TransitionHrEmployeeLifecycleCommand(created.Id, employee.Version,
                new HrEmployeeLifecycleTransitionData("Exited", effectiveDate, "Exit")),
            CancellationToken.None));

        var periods = await _db.HrEmploymentPeriods.Where(row => row.EmployeeId == created.Id)
            .OrderBy(row => row.StartDate).ToArrayAsync();
        Assert.Single(periods);
        Assert.Equal("Active", periods[0].Status);
        Assert.Null(periods[0].EndDate);
        Assert.Equal(6, await _db.HrEmployeeLifecycleEvents.CountAsync(row => row.EmployeeId == created.Id));
        Assert.Equal("EmploymentTypeChanged", typeChanged!.EventType);
        Assert.Equal("Probation", probation!.EventType);
        Assert.Equal("Confirmed", confirmed!.EventType);
        var finalEmployee = await _db.HrEmployees.SingleAsync(row => row.Id == created.Id);
        Assert.Equal("Active", finalEmployee.CurrentEmploymentStatus);
        Assert.Equal(12, finalEmployee.CurrentEmploymentTypeId);
    }

    [Fact]
    public async Task LifecycleRejectsInvalidTransitionsStaleVersionsAndImmutableHistoryMutation()
    {
        var created = await _create.Handle(new CreateHrEmployeeCommand(Data()), CancellationToken.None);
        var store = new HrEmployeeStore(_db);
        var lifecycle = new TransitionHrEmployeeLifecycleCommandHandler(_access, store, _auditTransaction);
        var invalid = new TransitionHrEmployeeLifecycleCommand(
            created.Id, created.Version, new HrEmployeeLifecycleTransitionData(
                "Reinstated", DateOnly.FromDateTime(DateTime.UtcNow), null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lifecycle.Handle(invalid, CancellationToken.None));
        Assert.Equal(created.Version, (await _db.HrEmployees.SingleAsync()).Version);

        var valid = await lifecycle.Handle(new TransitionHrEmployeeLifecycleCommand(
            created.Id, created.Version, new HrEmployeeLifecycleTransitionData(
                "Probation", DateOnly.FromDateTime(DateTime.UtcNow), null)), CancellationToken.None);
        await Assert.ThrowsAsync<HrEmployeeConcurrencyException>(() =>
            lifecycle.Handle(new TransitionHrEmployeeLifecycleCommand(
                created.Id, created.Version, new HrEmployeeLifecycleTransitionData(
                    "Confirmed", DateOnly.FromDateTime(DateTime.UtcNow), null)), CancellationToken.None));

        var history = await _db.HrEmployeeLifecycleEvents.SingleAsync(row => row.Id == valid!.Id);
        history.Reason = "rewritten";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task LifecycleAuditFailureRollsBackStatusAndHistory()
    {
        var created = await _create.Handle(new CreateHrEmployeeCommand(Data()), CancellationToken.None);
        _failure.Fail = true;
        var lifecycle = new TransitionHrEmployeeLifecycleCommandHandler(
            _access, new HrEmployeeStore(_db), _auditTransaction);

        await Assert.ThrowsAsync<IOException>(() => lifecycle.Handle(
            new TransitionHrEmployeeLifecycleCommand(created.Id, created.Version,
                new HrEmployeeLifecycleTransitionData(
                    "Probation", DateOnly.FromDateTime(DateTime.UtcNow), "Audit rollback")),
            CancellationToken.None));

        _db.ChangeTracker.Clear();
        Assert.Equal("Active", (await _db.HrEmployees.SingleAsync()).CurrentEmploymentStatus);
        Assert.Single(await _db.HrEmployeeLifecycleEvents.ToArrayAsync());
        Assert.Single(await _db.AuditEvents.ToArrayAsync());
    }

    [Fact]
    public async Task PositionHistoryEnforcesBusinessOrgUnitManagerAndVersionRules()
    {
        var employee = await _create.Handle(new CreateHrEmployeeCommand(Data()), CancellationToken.None);
        var manager = await _create.Handle(
            new CreateHrEmployeeCommand(Data() with { EmployeeCode = "EMP-2" }), CancellationToken.None);
        var (orgUnit, position) = await AddPositionAsync(1, "OPS-1");
        var (_, foreignPosition) = await AddPositionAsync(2, "OPS-FOREIGN");
        var store = new HrEmployeeStore(_db);
        var assign = new AssignHrEmployeePositionCommandHandler(_access, store, _auditTransaction);
        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var primaryData = new HrEmployeePositionAssignmentData(
            position.Id, orgUnit.Id, manager.Id, "Primary", effectiveDate, null, "Initial");

        var primary = await assign.Handle(new AssignHrEmployeePositionCommand(
            employee.Id, employee.Version, primaryData), CancellationToken.None);
        var currentEmployee = await _db.HrEmployees.SingleAsync(row => row.Id == employee.Id);
        Assert.Equal(position.Id, currentEmployee.CurrentPositionId);
        Assert.Equal(orgUnit.Id, currentEmployee.CurrentOrgUnitId);
        var staleVersion = currentEmployee.Version;
        var nextPosition = new HrPosition
        {
            BusinessId = 1,
            Code = "OPS-2",
            Name = "Position OPS-2",
            OrgUnitId = orgUnit.Id,
            Status = HrPositionStatus.Vacant,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Version = 1
        };
        _db.HrPositions.Add(nextPosition);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => assign.Handle(
            new AssignHrEmployeePositionCommand(employee.Id, currentEmployee.Version,
                primaryData with { PositionId = foreignPosition.Id }),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => assign.Handle(
            new AssignHrEmployeePositionCommand(employee.Id, currentEmployee.Version,
                primaryData with { OrgUnitId = orgUnit.Id + 1 }),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => assign.Handle(
            new AssignHrEmployeePositionCommand(employee.Id, currentEmployee.Version,
                primaryData with { ReportsToEmployeeId = employee.Id }),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => assign.Handle(
            new AssignHrEmployeePositionCommand(employee.Id, currentEmployee.Version, primaryData),
            CancellationToken.None));

        var acting = await assign.Handle(new AssignHrEmployeePositionCommand(
            employee.Id, currentEmployee.Version,
            primaryData with { AssignmentType = "Acting", ReportsToEmployeeId = null,
                EffectiveTo = effectiveDate }), CancellationToken.None);
        Assert.Equal("Acting", acting!.AssignmentType);
        Assert.Equal(position.Id, (await _db.HrEmployees.SingleAsync(row => row.Id == employee.Id))
            .CurrentPositionId);
        currentEmployee = await _db.HrEmployees.SingleAsync(row => row.Id == employee.Id);
        var changedPrimary = await assign.Handle(new AssignHrEmployeePositionCommand(
            employee.Id, currentEmployee.Version,
            primaryData with
            {
                PositionId = nextPosition.Id,
                AssignmentType = "Primary",
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                Reason = "Position change"
            }), CancellationToken.None);
        Assert.Equal(nextPosition.Id, changedPrimary!.PositionId);
        Assert.Equal(nextPosition.Id, (await _db.HrEmployees.SingleAsync(row => row.Id == employee.Id))
            .CurrentPositionId);
        Assert.Equal<DateOnly?>(effectiveDate, (await _db.HrEmployeePositionAssignments
            .SingleAsync(row => row.Id == primary!.Id)).EffectiveTo);
        await Assert.ThrowsAsync<HrEmployeeConcurrencyException>(() => assign.Handle(
            new AssignHrEmployeePositionCommand(employee.Id, staleVersion,
                primaryData with { AssignmentType = "Temporary", ReportsToEmployeeId = null }),
            CancellationToken.None));

        var managerRow = await _db.HrEmployees.SingleAsync(row => row.Id == manager.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => assign.Handle(
            new AssignHrEmployeePositionCommand(manager.Id, managerRow.Version,
                primaryData with { ReportsToEmployeeId = employee.Id, OrgUnitId = orgUnit.Id }),
            CancellationToken.None));
        Assert.Equal(3, await _db.HrEmployeePositionAssignments.CountAsync());
        Assert.True(await _db.HrEmployeeLifecycleEvents.CountAsync(row =>
            row.EmployeeId == employee.Id && row.EventType == "ManagerChanged") >= 1);
    }

    [Fact]
    public async Task DisabledHrAccessIsDeniedBeforePersistence()
    {
        _access.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _create.Handle(
            new CreateHrEmployeeCommand(Data()), CancellationToken.None));
        Assert.Empty(await _db.HrEmployees.ToListAsync());
        Assert.Empty(await _db.AuditEvents.ToListAsync());
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static HrEmployeeCreateData Data() => new(
        "EMP-1", "Alex", null, "Employee", "Alex Employee",
        new DateOnly(1990, 1, 1), "X", "NID-123", null, "alex@example.invalid",
        "alex.work@example.invalid", "555-0101", "Present address", "Permanent address",
        "Emergency Contact", "555-0199", 11, "Active",
        new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1));

    private void SetAuditBusiness(long businessId) =>
        _auditContext.Initialize(new AuditContextSnapshot(
            Guid.NewGuid(), 101, 101, 5, 7, null, businessId, AuditSource.API,
            "POST", "/api/hr/v1/employees", null, null, null, false, null, null, null, null,
            "employee-creator", "HR"));

    private async Task<(HrOrgUnit OrgUnit, HrPosition Position)> AddPositionAsync(
        long businessId, string code)
    {
        if (businessId != _access.BusinessId)
            SetAuditBusiness(businessId);
        var now = DateTime.UtcNow;
        var orgUnit = new HrOrgUnit
        {
            BusinessId = businessId,
            Code = $"UNIT-{code}",
            Name = $"Unit {code}",
            UnitType = "Department",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            CreatedAtUtc = now,
            CreatedByUserId = 101,
            UpdatedAtUtc = now,
            UpdatedByUserId = 101
        };
        _db.HrOrgUnits.Add(orgUnit);
        await _db.SaveChangesAsync();
        var position = new HrPosition
        {
            BusinessId = businessId,
            Code = code,
            Name = $"Position {code}",
            OrgUnitId = orgUnit.Id,
            Status = HrPositionStatus.Vacant,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Version = 1
        };
        _db.HrPositions.Add(position);
        await _db.SaveChangesAsync();
        SetAuditBusiness(_access.BusinessId);
        return (orgUnit, position);
    }

    private sealed class TestAccess : IHrEmployeeAccess
    {
        public long BusinessId { get; set; } = 1;
        public bool Allowed { get; set; } = true;
        public HrEmployeeFieldAccess FieldAccess { get; set; } = new(true, true);

        public long RequireBusinessId() => BusinessId;
        public long RequireRealUserId() => 101;
        public HrEmployeeFieldAccess GetFieldAccess() => FieldAccess;
        public Task EnsureAsync(
            long businessId, HrEmployeeOperation operation, CancellationToken cancellationToken) =>
            Allowed && businessId == BusinessId
                ? Task.CompletedTask
                : throw new UnauthorizedAccessException("HR is disabled or permission is missing.");

        public Task EnsurePositionAssignmentAsync(
            long businessId, long? locationProjectionId, CancellationToken cancellationToken) =>
            Allowed && businessId == BusinessId
                ? Task.CompletedTask
                : throw new UnauthorizedAccessException("Position assignment is not permitted.");
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
                throw new IOException("Injected employee Audit v2 persistence failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
