using RetailPOS.Application.AccountingJournals.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.OtherIncome.Abstractions;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.OtherIncome;

namespace RetailPOS.Tests.Accounting;

public class OtherIncomeAtomicityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RetailPOSDbContext _db;
    private readonly Mock<ITenantAccessService> _tenant = new();
    private readonly Mock<IJournalNumberAllocator> _journalNumbers = new();

    public OtherIncomeAtomicityTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA ignore_check_constraints = 1;";
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RetailPOSDbContext(options);
        _db.Database.EnsureCreated();
        Seed();

        _tenant.Setup(x => x.IsSuperAdmin).Returns(false);
        _tenant.Setup(x => x.EffectiveBusinessId).Returns(1);
        _tenant.Setup(x => x.RequireBusinessId()).Returns(1);

        _journalNumbers
            .Setup(x => x.AllocateNextJournalNumberAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<DateTime>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("JR-2026-000001");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ApproveAsync_RollsBack_PostedJournal_WhenApprovalAuditFails()
    {
        var audit = new Mock<IAuditService>();
        audit
            .Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .Returns<AuditEventInput, CancellationToken>((input, _) =>
            {
                if (input.Summary.Contains("Other income approved", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Synthetic audit failure after approval");
                }

                return Task.CompletedTask;
            });

        audit
            .Setup(x => x.RecordFailureAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var configurationService = AccountingConfigurationServiceTestFactory.Create(
            _db,
            _tenant.Object,
            NullLogger<AccountingConfigurationService>.Instance);
        var journalStore = new ManualJournalDraftStore(
            _db,
            audit.Object,
            _journalNumbers.Object,
            NullLogger<ManualJournalDraftStore>.Instance);
        var store = new OtherIncomeCommandStore(
            _db,
            audit.Object,
            NullLogger<OtherIncomeCommandStore>.Instance,
            new OtherIncomeApprovalJournalPoster(
                SupplierPaymentCreationTestHarness.CreateJournalCommandSender(journalStore),
                _tenant.Object),
            new OtherIncomePostingAccountResolver(configurationService, _tenant.Object));

        var entryId = await store.CreateAsync(
            1,
            101,
            new OtherIncomeCreationInput(
                new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc),
                "Commission",
                null,
                65m,
                null,
                null,
                null,
                null,
                null,
                1021,
                null,
                null,
                null),
            CancellationToken.None);

        await store.SubmitAsync(1, 101, entryId, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ApproveAsync(1, 101, entryId, null, CancellationToken.None));

        _db.ChangeTracker.Clear();

        var entry = await _db.OtherIncomeEntries.FirstAsync(x => x.Id == entryId);
        Assert.Equal(OtherIncomeStatuses.Submitted, entry.Status);
        Assert.Null(entry.JournalEntryId);
        Assert.Equal(0, await _db.JournalEntries.CountAsync(x => x.ReferenceType == "OtherIncome"));
        Assert.Equal(0, await _db.JournalEntryLines.CountAsync());
    }

    private void Seed()
    {
        _db.Businesses.Add(new Business { Id = 1, Name = "Other Income Atomicity Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main Outlet", Address = "X", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

        _db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1021, "1020", "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "2000", "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(3001, "3000", "Owner Capital", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(4001, "4000", "Sales", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(4011, "4010", "Sales Discount", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(4021, "4020", "Sales Return", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(5001, "5000", "COGS", ChartAccountTypes.CostOfGoodsSold, NormalBalanceTypes.Debit),
            Account(5101, "5100", "Inventory Shortage", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(5201, "5200", "Inventory Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6001, "6000", "Other Income", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6101, "6100", "Rounding Difference", ChartAccountTypes.Expense, NormalBalanceTypes.Debit),
            Account(6201, "6200", "Cash Difference Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(6211, "6210", "Cash Difference Loss", ChartAccountTypes.Expense, NormalBalanceTypes.Debit)
        );

        _db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            CashAccountId = 1001,
            PettyCashAccountId = 1001,
            DefaultBankAccountId = 1021,
            InventoryAccountId = 1001,
            SalesAccountId = 4001,
            SalesDiscountAccountId = 4011,
            SalesReturnAccountId = 4021,
            CogsAccountId = 5001,
            InventoryShortageAccountId = 5101,
            InventoryGainAccountId = 5201,
            SupplierPayableAccountId = 2001,
            CustomerAdvanceAccountId = 2001,
            OtherIncomeAccountId = 6001,
            OwnerCapitalAccountId = 3001,
            OwnerDrawingsAccountId = 3001,
            RoundingDifferenceAccountId = 6101,
            CashDifferenceGainAccountId = 6201,
            CashDifferenceLossAccountId = 6211,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.FinancialYears.Add(new FinancialYear
        {
            Id = 9301,
            BusinessId = 1,
            YearCode = "FY26",
            Name = "FY26",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            IsCurrent = true,
            IsClosed = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 9302,
            FinancialYearId = 9301,
            PeriodNumber = 4,
            Name = "April",
            StartDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    private static ChartAccount Account(long id, string code, string name, string type, string normalBalance)
    {
        return new ChartAccount
        {
            Id = id,
            BusinessId = 1,
            AccountCode = code,
            AccountName = name,
            AccountType = type,
            NormalBalanceType = normalBalance,
            IsSystemAccount = false,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}