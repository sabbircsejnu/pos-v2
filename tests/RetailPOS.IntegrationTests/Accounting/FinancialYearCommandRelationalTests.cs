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

public class FinancialYearCommandRelationalTests
{
    [Fact]
    public async Task Commands_AtomicallyHandOverCurrentYear_AndRollbackFailedCreate()
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

        await using (var db = new RetailPOSDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Businesses.Add(new Business
            {
                Id = 1,
                Name = "Financial Year Relational Business",
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
            await db.SaveChangesAsync();

            var store = new FinancialYearCommandStore(
                db,
                audit.Object,
                NullLogger<FinancialYearCommandStore>.Instance);
            var tenantScope = new TestFinancialPeriodTenantScope(1);
            var createHandler = new CreateFinancialYearCommandHandler(store, tenantScope);
            var updateHandler = new UpdateFinancialYearCommandHandler(store, tenantScope);
            var activateHandler = new ActivateFinancialYearCommandHandler(store, tenantScope);

            var created = await createHandler.Handle(
                new CreateFinancialYearCommand(
                    "FY2027",
                    "FY 2027",
                    new DateTime(2027, 1, 1),
                    new DateTime(2027, 12, 31),
                    IsCurrent: true,
                    ActorUserId: 901),
                CancellationToken.None);
            Assert.True(created.IsCurrent);

            var updated = await updateHandler.Handle(
                new UpdateFinancialYearCommand(
                    created.Id,
                    "FY2027-UPDATED",
                    "FY 2027 Updated",
                    new DateTime(2027, 1, 1),
                    new DateTime(2027, 12, 31),
                    ActorUserId: 902),
                CancellationToken.None);
            Assert.Equal("FY2027-UPDATED", updated.YearCode);

            var activated = await activateHandler.Handle(
                new ActivateFinancialYearCommand(2026, ActorUserId: 903),
                CancellationToken.None);
            Assert.True(activated.IsCurrent);

            await Assert.ThrowsAsync<DbUpdateException>(() => createHandler.Handle(
                new CreateFinancialYearCommand(
                    "FY2028",
                    "FY 2026",
                    new DateTime(2028, 1, 1),
                    new DateTime(2028, 12, 31),
                    IsCurrent: true,
                    ActorUserId: 904),
                CancellationToken.None));
        }

        await using var verificationDb = new RetailPOSDbContext(options);
        var persistedYears = await verificationDb.FinancialYears
            .AsNoTracking()
            .OrderBy(year => year.Id)
            .ToListAsync();

        Assert.Equal(2, persistedYears.Count);
        Assert.Equal(1, persistedYears.Count(year => year.IsCurrent));
        Assert.True(persistedYears.Single(year => year.Id == 2026).IsCurrent);
        Assert.False(persistedYears.Single(year => year.Id != 2026).IsCurrent);
        Assert.Equal(903L, persistedYears.Single(year => year.Id != 2026).UpdatedBy);
        audit.Verify(service => service.RecordAsync(
            It.IsAny<AuditEventInput>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
        audit.Verify(service => service.RecordFailureAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

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
