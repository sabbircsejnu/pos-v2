using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.HR.Attendance;
using RetailPOS.Application.HR.Calendars;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Attendance;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrDevicePersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private RetailPOSDbContext db = null!;
    private HrDeviceHandlers handler = null!;
    private HrDeviceStore store = null!;
    private readonly Failure failure = new();
    internal static HrDeviceData Data() => new("DEVICE", "Serial", "Vendor", "Terminal", 10, "ADMS", "Asia/Dhaka",
        CredentialSecretReference: "device-secret");
    internal static DateTimeOffset Start => new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public async Task StoreRechecksDeviceAndEmployeeScopeInsideTransaction()
    {
        var d = await handler.Handle(new SaveHrDeviceCommand(null, null, Data()), default);
        await using var tx = await db.Database.BeginTransactionAsync();
        var denied = new RetailPOS.Application.HR.Organisation.HrOperationalLocationReadScope(1, [new("Outlet", "999")]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(1, d.Id, d.Version, Data(), false, denied, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.CreateMappingAsync(1, d.Id, new(1, "23", Start), denied, default));
        Assert.Empty(await db.Set<HrDeviceUserMapping>().ToListAsync());
        await tx.RollbackAsync();
    }
    public async Task InitializeAsync()
    {
        await connection.OpenAsync(); var context = new AuditContext();
        db = new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(connection)
            .AddInterceptors(new AuditChangeTrackerInterceptor(context, NullLogger<AuditChangeTrackerInterceptor>.Instance), failure).Options);
        await db.Database.EnsureCreatedAsync(); await HrRosterPersistenceTests.Seed(db);
        context.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1, AuditSource.API,
            "POST", "/api/hr/v1/attendance/devices", null, null, null, false, null, null, null, null, "actor", "HR"));
        store = new(db, TimeProvider.System);
        handler = new(new HrCalendarPersistenceTests.Access(), store, new RequiredAuditTransaction(db, context,
            new AuditService(db, context, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance), NullLogger<RequiredAuditTransaction>.Instance));
    }
    [Fact]
    public async Task DevicePreservesSecretNameButDoesNotExposeItAndRejectsStaleWrite()
    {
        var d = await handler.Handle(new SaveHrDeviceCommand(null, null, Data()), default);
        Assert.True(d.HasCredentialReference);
        Assert.DoesNotContain("device-secret", System.Text.Json.JsonSerializer.Serialize(d));
        d = await handler.Handle(new SaveHrDeviceCommand(d.Id, d.Version, Data() with { Model = "New", CredentialSecretReference = null }), default);
        Assert.True(d.HasCredentialReference);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handler.Handle(new SaveHrDeviceCommand(d.Id, 1, Data()), default));
        d = await handler.Handle(new SaveHrDeviceCommand(d.Id, d.Version, Data() with { CredentialSecretReference = null }, true), default);
        Assert.False(d.HasCredentialReference);
        Assert.DoesNotContain("device-secret", string.Join("", await db.AuditEventFieldChanges.Select(f => f.OldValue + f.NewValue).ToArrayAsync()));
    }
    [Theory]
    [InlineData(20)] [InlineData(999)]
    public async Task ForeignUnknownLocationRejected(long location)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new SaveHrDeviceCommand(null, null, Data() with { OperationalLocationProjectionId = location }), default));
        Assert.Empty(await db.Set<HrAttendanceDevice>().ToListAsync()); Assert.Empty(await db.AuditEvents.ToListAsync());
    }
    [Fact]
    public async Task DeploymentWideVendorSerialAndBusinessCodeUnique()
    {
        await handler.Handle(new SaveHrDeviceCommand(null, null, Data()), default);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handler.Handle(new SaveHrDeviceCommand(null, null,
            Data() with { DeviceCode = "OTHER" }), default));
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handler.Handle(new SaveHrDeviceCommand(null, null,
            Data() with { SerialNumber = "OTHER" }), default));
        await using var tx = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => store.SaveAsync(2, null, null, Data(), false,
            new RetailPOS.Application.HR.Organisation.HrOperationalLocationReadScope(2, null), default));
        await tx.RollbackAsync();
    }
    [Fact]
    public async Task MappingHistoryUsesHalfOpenUtcIntervalsAndCannotBeDeleted()
    {
        var d = await handler.Handle(new SaveHrDeviceCommand(null, null, Data()), default);
        var first = await handler.Handle(new CreateHrDeviceMappingCommand(d.Id, new(1, "23", Start)), default);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handler.Handle(new CreateHrDeviceMappingCommand(d.Id, new(1, "23", Start.AddDays(1))), default));
        var end = Start.AddDays(2);
        var closed = await handler.Handle(new CloseHrDeviceMappingCommand(first.Id, first.Version, end), default);
        Assert.False(closed.IsActive); Assert.Equal(end, closed.EffectiveToUtc);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handler.Handle(new CloseHrDeviceMappingCommand(first.Id, first.Version, end), default));
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handler.Handle(new CreateHrDeviceMappingCommand(d.Id, new(1, "23", end.AddMinutes(-1), end.AddMinutes(1))), default));
        var second = await handler.Handle(new CreateHrDeviceMappingCommand(d.Id, new(1, "23", end)), default);
        Assert.NotEqual(first.Id, second.Id); Assert.Equal(2, (await store.MappingsAsync(1, d.Id, default)).Count);
        db.Remove(await db.Set<HrDeviceUserMapping>().FirstAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Theory]
    [InlineData("foreign-employee")] [InlineData("wrong-location")] [InlineData("not-employed")] [InlineData("disabled-device")]
    public async Task MappingRequiresSameBusinessEmploymentAndDeviceLocation(string kind)
    {
        var d = await handler.Handle(new SaveHrDeviceCommand(null, null, Data()), default);
        if (kind == "disabled-device") await handler.Handle(new DeactivateHrDeviceCommand(d.Id, d.Version), default);
        if (kind == "wrong-location") (await db.HrEmployeeLocationAssignments.SingleAsync()).EffectiveTo = new(2026, 12, 30);
        if (kind == "not-employed") (await db.HrEmploymentPeriods.SingleAsync()).EndDate = new(2026, 12, 30);
        await db.SaveChangesAsync(); var count = await db.AuditEvents.CountAsync();
        var error = await Record.ExceptionAsync(() => handler.Handle(new CreateHrDeviceMappingCommand(d.Id,
            new(kind == "foreign-employee" ? 999 : 1, "23", Start)), default));
        Assert.True(error is ArgumentException or KeyNotFoundException or HrCalendarConflictException, error?.ToString());
        Assert.Empty(await db.Set<HrDeviceUserMapping>().ToListAsync()); Assert.Equal(count, await db.AuditEvents.CountAsync());
    }
    [Fact]
    public async Task MappedIdentityTimezoneAndLocationCannotBeRewritten()
    {
        var d = await handler.Handle(new SaveHrDeviceCommand(null, null, Data()), default);
        await handler.Handle(new CreateHrDeviceMappingCommand(d.Id, new(1, "23", Start)), default);
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handler.Handle(new SaveHrDeviceCommand(d.Id, d.Version,
            Data() with { DeviceTimeZoneId = "UTC" }), default));
        await Assert.ThrowsAsync<HrCalendarConflictException>(() => handler.Handle(new SaveHrDeviceCommand(d.Id, d.Version,
            Data() with { SerialNumber = "Other" }), default));
        Assert.Equal(1, (await store.DeviceAsync(1, d.Id, default)).Version);
    }
    [Fact]
    public async Task AuditFailureRollsBackDeviceAndMappingClosure()
    {
        failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => handler.Handle(new SaveHrDeviceCommand(null, null, Data()), default));
        db.ChangeTracker.Clear(); Assert.Empty(await db.Set<HrAttendanceDevice>().ToListAsync());
        failure.Fail = false;
        var d = await handler.Handle(new SaveHrDeviceCommand(null, null, Data()), default);
        var m = await handler.Handle(new CreateHrDeviceMappingCommand(d.Id, new(1, "23", Start)), default);
        failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => handler.Handle(new CloseHrDeviceMappingCommand(m.Id, m.Version, Start.AddDays(1)), default));
        db.ChangeTracker.Clear(); var retained = await db.Set<HrDeviceUserMapping>().SingleAsync();
        Assert.True(retained.IsActive); Assert.Null(retained.EffectiveToUtc); Assert.Equal(1, retained.Version);
    }
    [Fact]
    public async Task DeviceTimeZoneDeterminesMappingAssignmentDateNotUtcDate()
    {
        var prior = await db.HrEmployeeLocationAssignments.SingleAsync();
        prior.EffectiveTo = new(2027, 1, 1); prior.Status = "Closed"; prior.Version++;
        await db.SaveChangesAsync();
        db.Outlets.Add(new RetailPOS.Core.Entities.Outlet { Id = 30, BusinessId = 1, Name = "New location" });
        db.OperationalLocationProjections.Add(new RetailPOS.Core.Entities.HR.Organisation.OperationalLocationProjection {
            Id = 30, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet", SourceEntityId = "30",
            Name = "New location", SourceVersion = 1 });
        db.HrEmployeeLocationAssignments.Add(new RetailPOS.Core.Entities.HR.Employees.HrEmployeeLocationAssignment {
            BusinessId = 1, EmployeeId = 1, OperationalLocationProjectionId = 30, EffectiveFrom = new(2027, 1, 2),
            LocationNameSnapshot = "New location", SourceSystemSnapshot = "RetailPOS", SourceEntityTypeSnapshot = "Outlet",
            SourceEntityIdSnapshot = "30", LocationSourceVersionSnapshot = 1, RequestedByUserId = 101, RequestedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var d = await handler.Handle(new SaveHrDeviceCommand(null, null, Data() with { OperationalLocationProjectionId = 30 }), default);
        var instant = new DateTimeOffset(2027, 1, 1, 19, 0, 0, TimeSpan.Zero);
        var mapping = await handler.Handle(new CreateHrDeviceMappingCommand(d.Id, new(1, "23", instant)), default);
        Assert.Equal(instant, mapping.EffectiveFromUtc);
    }
    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    private sealed class Failure : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData d, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Fail && d.Context!.ChangeTracker.Entries<AuditEvent>().Any(e => e.State == EntityState.Added)) throw new IOException("Injected device audit failure.");
            return base.SavingChangesAsync(d, result, ct);
        }
    }
}
