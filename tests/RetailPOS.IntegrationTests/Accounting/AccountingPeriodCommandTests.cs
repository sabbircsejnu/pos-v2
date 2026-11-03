using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.FinancialPeriods.Abstractions;
using RetailPOS.Application.FinancialPeriods.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.FinancialPeriods;

namespace RetailPOS.Tests.Accounting;

public class AccountingPeriodCommandTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<IAuditService> _audit = new();

    public AccountingPeriodCommandTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new RetailPOSDbContext(options);
        Seed();

        _audit
            .Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _audit
            .Setup(x => x.RecordFailureAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task UpdatePeriodStatusCommand_RequiresReason_WhenLocking()
    {
        var handler = CreateStatusHandler();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new UpdateAccountingPeriodStatusCommand(
                    4101,
                    AccountingPeriodStatuses.Locked,
                    null,
                    ActorUserId: 901),
                CancellationToken.None));

        Assert.Contains("Lock reason is required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdatePeriodStatusCommand_RejectsLockedToClosedTransition()
    {
        var handler = CreateStatusHandler();

        await handler.Handle(
            new UpdateAccountingPeriodStatusCommand(
                4101,
                AccountingPeriodStatuses.Locked,
                "Year-end freeze",
                ActorUserId: 901),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new UpdateAccountingPeriodStatusCommand(
                    4101,
                    AccountingPeriodStatuses.Closed,
                    "Close after lock",
                    ActorUserId: 901),
                CancellationToken.None));

        Assert.Contains("must be reopened before it can be closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreatePeriodCommand_RejectsNonPositivePeriodNumber()
    {
        var store = new AccountingPeriodCommandStore(
            _db,
            _audit.Object,
            NullLogger<AccountingPeriodCommandStore>.Instance);
        var handler = new CreateAccountingPeriodCommandHandler(
            store,
            new TestFinancialPeriodTenantScope(1));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CreateAccountingPeriodCommand(
                4001,
                0,
                "Invalid",
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
                AccountingPeriodStatuses.Open,
                ActorUserId: 901), CancellationToken.None));

        Assert.Contains("greater than zero", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdatePeriodStatusCommand_WritesAuditEvent_OnSuccessfulTransition()
    {
        var handler = CreateStatusHandler();

        var updated = await handler.Handle(
            new UpdateAccountingPeriodStatusCommand(
                4101,
                AccountingPeriodStatuses.Locked,
                "Compliance hold",
                ActorUserId: 901),
            CancellationToken.None);

        Assert.Equal(AccountingPeriodStatuses.Locked, updated.Status);

        _audit.Verify(x => x.RecordAsync(
            It.Is<AuditEventInput>(input =>
                input.Module == RetailPOS.Core.Entities.Audit.AuditModule.Accounts
                && input.Summary.Contains("Accounting period status changed", StringComparison.Ordinal)
                && input.PrimaryEntity.HasValue
                && input.PrimaryEntity.Value.Type == "AccountingPeriod"
                && input.PrimaryEntity.Value.Id == "4101"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private UpdateAccountingPeriodStatusCommandHandler CreateStatusHandler()
    {
        var store = new AccountingPeriodCommandStore(
            _db,
            _audit.Object,
            NullLogger<AccountingPeriodCommandStore>.Instance);
        return new UpdateAccountingPeriodStatusCommandHandler(
            store,
            new TestFinancialPeriodTenantScope(1));
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

    private void Seed()
    {
        _db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "FY Test Business",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.FinancialYears.Add(new FinancialYear
        {
            Id = 4001,
            BusinessId = 1,
            YearCode = "FY2026",
            Name = "FY 2026",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            IsCurrent = true,
            IsClosed = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 4101,
            FinancialYearId = 4001,
            PeriodNumber = 1,
            Name = "January",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();
    }
}
