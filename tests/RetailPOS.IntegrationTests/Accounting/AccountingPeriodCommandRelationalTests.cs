using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.FinancialPeriods.Abstractions;
using RetailPOS.Application.FinancialPeriods.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.FinancialPeriods;

namespace RetailPOS.Tests.Accounting;

public class AccountingPeriodCommandRelationalTests
{
    [Fact]
    public async Task CreateAndUpdate_UseRelationalPersistence_AndFailedDuplicateRollsBack()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;

        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        audit.Setup(service => service.RecordFailureAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        long createdId;
        await using (var db = new RetailPOSDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Businesses.Add(new Business
            {
                Id = 1,
                Name = "Accounting Period Relational Business",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.FinancialYears.Add(new FinancialYear
            {
                Id = 2026,
                BusinessId = 1,
                YearCode = "FY2026",
                Name = "FY 2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 12, 31),
                IsCurrent = true,
                IsClosed = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.AccountingPeriods.AddRange(
                CreateSeedPeriod(1, 1, "January", 1, 31),
                CreateSeedPeriod(2, 2, "February", 32, 59));
            await db.SaveChangesAsync();

            var store = new AccountingPeriodCommandStore(
                db,
                audit.Object,
                NullLogger<AccountingPeriodCommandStore>.Instance);
            var tenantScope = new TestFinancialPeriodTenantScope(1);
            var createHandler = new CreateAccountingPeriodCommandHandler(store, tenantScope);
            var updateHandler = new UpdateAccountingPeriodCommandHandler(store, tenantScope);
            var statusHandler = new UpdateAccountingPeriodStatusCommandHandler(store, tenantScope);

            var created = await createHandler.Handle(
                new CreateAccountingPeriodCommand(
                    2026,
                    3,
                    " March ",
                    new DateTime(2026, 3, 1),
                    new DateTime(2026, 3, 31),
                    "OPEN",
                    ActorUserId: 901),
                CancellationToken.None);
            createdId = created.Id;
            Assert.Equal("March", created.Name);
            Assert.Equal(AccountingPeriodStatuses.Open, created.Status);

            var updated = await updateHandler.Handle(
                new UpdateAccountingPeriodCommand(
                    created.Id,
                    4,
                    " March Updated ",
                    new DateTime(2026, 3, 1),
                    new DateTime(2026, 3, 31),
                    ActorUserId: 902),
                CancellationToken.None);
            Assert.Equal(4, updated.PeriodNumber);
            Assert.Equal("March Updated", updated.Name);

            var locked = await statusHandler.Handle(
                new UpdateAccountingPeriodStatusCommand(
                    created.Id,
                    AccountingPeriodStatuses.Locked,
                    " Month-end freeze ",
                    ActorUserId: 903),
                CancellationToken.None);
            Assert.Equal(AccountingPeriodStatuses.Locked, locked.Status);
            Assert.Equal("Month-end freeze", locked.StatusReason);
            Assert.Equal(903L, locked.LockedBy);

            var reopened = await statusHandler.Handle(
                new UpdateAccountingPeriodStatusCommand(
                    created.Id,
                    AccountingPeriodStatuses.Open,
                    " Adjustment window ",
                    ActorUserId: 904),
                CancellationToken.None);
            Assert.Equal(AccountingPeriodStatuses.Open, reopened.Status);
            Assert.Equal("Adjustment window", reopened.StatusReason);
            Assert.Equal(904L, reopened.ReopenedBy);

            await Assert.ThrowsAsync<DbUpdateException>(() => createHandler.Handle(
                new CreateAccountingPeriodCommand(
                    2026,
                    2,
                    "Duplicate Period Number",
                    new DateTime(2026, 4, 1),
                    new DateTime(2026, 4, 30),
                    AccountingPeriodStatuses.Open,
                    ActorUserId: 903),
                CancellationToken.None));
        }

        await using var verificationDb = new RetailPOSDbContext(options);
        var periods = await verificationDb.AccountingPeriods
            .AsNoTracking()
            .OrderBy(period => period.PeriodNumber)
            .ToListAsync();

        Assert.Equal(3, periods.Count);
        var persistedUpdate = periods.Single(period => period.Id == createdId);
        Assert.Equal(4, persistedUpdate.PeriodNumber);
        Assert.Equal("March Updated", persistedUpdate.Name);
        Assert.Equal(AccountingPeriodStatuses.Open, persistedUpdate.Status);
        Assert.Equal("Adjustment window", persistedUpdate.StatusReason);
        Assert.Equal(904L, persistedUpdate.UpdatedBy);
        Assert.Equal(903L, persistedUpdate.LockedBy);
        Assert.Equal(904L, persistedUpdate.ReopenedBy);
        Assert.DoesNotContain(periods, period => period.Name == "Duplicate Period Number");
        audit.Verify(service => service.RecordAsync(
            It.IsAny<AuditEventInput>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));
        audit.Verify(service => service.RecordFailureAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AccountingPeriod CreateSeedPeriod(
        long id,
        int periodNumber,
        string name,
        int startDay,
        int endDay) =>
        new()
        {
            Id = id,
            FinancialYearId = 2026,
            PeriodNumber = periodNumber,
            Name = name,
            StartDate = new DateTime(2026, 1, 1).AddDays(startDay - 1),
            EndDate = new DateTime(2026, 1, 1).AddDays(endDay - 1),
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private sealed class TestFinancialPeriodTenantScope(long businessId)
        : IFinancialPeriodTenantScope
    {
        public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(businessId);
        }
    }
}
