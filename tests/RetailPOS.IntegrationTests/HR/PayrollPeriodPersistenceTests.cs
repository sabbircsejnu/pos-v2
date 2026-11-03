using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollPeriodPersistenceTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly AuditContext auditContext = new();
    private readonly FailingAuditInterceptor failure = new();
    private RetailPOSDbContext db = null!;
    private PayrollPeriodStore store = null!;
    private RequiredAuditTransaction transaction = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(
                new AuditChangeTrackerInterceptor(auditContext,
                    NullLogger<AuditChangeTrackerInterceptor>.Instance),
                failure)
            .Options);
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "Payroll" },
            new Business { Id = 2, Name = "Other payroll" });
        await db.SaveChangesAsync();
        auditContext.Initialize(new AuditContextSnapshot(Guid.NewGuid(), 101, 101, 5, 7, null, 1,
            AuditSource.API, "POST", "/api/payroll/v1/periods", null, null, null,
            false, null, null, null, null, "actor", "Payroll"));
        store = new(db);
        transaction = new RequiredAuditTransaction(db, auditContext,
            new AuditService(db, auditContext, Mock.Of<IServiceProvider>(),
                NullLogger<AuditService>.Instance),
            NullLogger<RequiredAuditTransaction>.Instance);
    }

    [Fact]
    public async Task CreateOpensPeriodAndKeepsReadsBusinessScoped()
    {
        var period = await CreateAsync("2026-10", new(2026, 10, 1), new(2026, 10, 31));

        Assert.Equal("2026-10", period.PeriodCode);
        Assert.Equal("Open", period.Status);
        Assert.Equal(1, period.Version);
        Assert.Equal(101, period.CreatedByUserId);
        Assert.Equal(period.CreatedAtUtc, period.OpenedAtUtc);
        Assert.Equal(101, period.OpenedByUserId);
        Assert.Empty(await store.ListAsync(2, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.GetAsync(2, period.Id, default));
        Assert.Single(await db.AuditEvents.Where(row => row.BusinessId == 1).ToListAsync());
    }

    [Fact]
    public async Task InvalidDuplicateAndOverlappingPeriodsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateAsync("INVALID", new(2026, 11, 1), new(2026, 10, 31)));
        await CreateAsync("2026-10", new(2026, 10, 1), new(2026, 10, 31));

        await Assert.ThrowsAsync<PayrollPeriodConflictException>(() =>
            CreateAsync("2026-10", new(2026, 11, 1), new(2026, 11, 30)));
        await Assert.ThrowsAsync<PayrollPeriodConflictException>(() =>
            CreateAsync("2026-11", new(2026, 10, 31), new(2026, 11, 30)));

        Assert.Single(await db.PayrollPeriods.ToListAsync());
    }

    [Fact]
    public async Task ProcessingTransitionIsVersionedIdempotentAndRejectsStaleOrInvalidState()
    {
        var period = await CreateAsync("2026-10", new(2026, 10, 1), new(2026, 10, 31));
        await Assert.ThrowsAsync<PayrollPeriodConflictException>(() =>
            TransitionAsync(period.Id, expectedVersion: 0));

        var processing = await TransitionAsync(period.Id, period.Version);
        Assert.Equal("Processing", processing.Status);
        Assert.Equal(2, processing.Version);
        Assert.Equal(101, processing.ProcessingByUserId);
        var replay = await TransitionAsync(period.Id, period.Version);
        Assert.True(replay.IsReplay);
        Assert.Equal(processing.Version, replay.Version);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payroll_periods
                (business_id, period_code, period_start, period_end, status, version,
                 created_at_utc, created_by_user_id, opened_at_utc, opened_by_user_id)
            VALUES (1, 'CLOSED', '2026-12-01', '2026-12-31', 'Closed', 1,
                    '2026-10-01T00:00:00Z', 101, '2026-10-01T00:00:00Z', 101)
            """);
        var closed = await db.PayrollPeriods.AsNoTracking().SingleAsync(row => row.PeriodCode == "CLOSED");
        await Assert.ThrowsAsync<PayrollPeriodConflictException>(() =>
            TransitionAsync(closed.Id, closed.Version));
    }

    [Fact]
    public async Task PeriodDateEvidenceCannotBeEditedOrDeleted()
    {
        await CreateAsync("2026-10", new(2026, 10, 1), new(2026, 10, 31));
        var row = await db.PayrollPeriods.SingleAsync();
        row.PeriodStart = new(2026, 10, 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.Entry(row).State = EntityState.Deleted;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task RequiredAuditFailureRollsBackPeriodCreation()
    {
        failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() =>
            CreateAsync("2026-10", new(2026, 10, 1), new(2026, 10, 31)));

        Assert.Empty(await db.PayrollPeriods.ToListAsync());
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task DisabledPayrollCapabilityDeniesPeriodCreation()
    {
        var capabilities = new Mock<IHrPayrollCapabilityService>();
        capabilities.Setup(service => service.EnsureEnabledAsync(
                HrPayrollCapabilityKeys.Hr, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        capabilities.Setup(service => service.EnsureEnabledAsync(
                HrPayrollCapabilityKeys.Payroll, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Payroll is disabled."));
        var access = new Mock<IPayrollPeriodAccess>();
        access.Setup(value => value.RequireBusinessId()).Returns(1);
        access.Setup(value => value.RequireRealUserId()).Returns(101);
        var periodStore = new Mock<IPayrollPeriodStore>();
        var handlers = new PayrollPeriodHandlers(capabilities.Object, access.Object,
            periodStore.Object, transaction);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handlers.Handle(
            new CreatePayrollPeriodCommand(new("2026-10", new(2026, 10, 1), new(2026, 10, 31), null)),
            default));
        periodStore.Verify(store => store.CreateAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CreatePayrollPeriodRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private Task<PayrollPeriodResponse> CreateAsync(string code, DateOnly start, DateOnly end) =>
        transaction.ExecuteAsync(
            new RequiredAuditOperation(1, "payroll.period.created", "Payroll",
                "Created a Payroll period.", "PayrollPeriod", "new"),
            token => store.CreateAsync(1, 101,
                new CreatePayrollPeriodRequest(code, start, end, null), token), default);

    private Task<PayrollPeriodResponse> TransitionAsync(long id, long expectedVersion) =>
        transaction.ExecuteAsync(
            new RequiredAuditOperation(1, "payroll.period.processing_started", "Payroll",
                "Started Payroll period processing.", "PayrollPeriod", id.ToString()),
            token => store.StartProcessingAsync(1, 101, id, expectedVersion, token),
            result => !result.IsReplay, default);

    private sealed class FailingAuditInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context!.ChangeTracker.Entries<AuditEvent>()
                    .Any(entry => entry.State == EntityState.Added))
                throw new IOException("Injected Payroll period audit failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
