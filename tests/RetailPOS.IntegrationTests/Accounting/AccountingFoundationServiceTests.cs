using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.ChartAccounts.Commands;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingConfigurations;
using RetailPOS.Infrastructure.ChartAccounts;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public class AccountingFoundationServiceTests : IDisposable
{
    private readonly RetailPOSDbContext _db;

    public AccountingFoundationServiceTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new RetailPOSDbContext(options);
        SeedBusinessAndOutlet();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Provisioner_InitializesDefaultChartAccountsIdempotently()
    {
        var provisioner = new AccountingConfigurationProvisioner(
            _db,
            NullLogger<AccountingConfigurationProvisioner>.Instance);

        await provisioner.EnsureInitializedAsync(1, 9001, CancellationToken.None);
        var first = await _db.ChartAccounts.Where(account => account.BusinessId == 1).ToListAsync();
        await provisioner.EnsureInitializedAsync(1, 9001, CancellationToken.None);
        var second = await _db.ChartAccounts.Where(account => account.BusinessId == 1).ToListAsync();

        Assert.Equal(AccountingDefaults.ChartOfAccounts.Count, first.Count);
        Assert.Equal(first.Count, second.Count);

        var cash = first.SingleOrDefault(x => x.AccountCode == AccountingDefaults.CashOnHandCode);
        Assert.NotNull(cash);
        Assert.Equal(ChartAccountTypes.Asset, cash!.AccountType);

        var retainedEarnings = first.SingleOrDefault(x => x.AccountCode == AccountingDefaults.RetainedEarningsCode);
        Assert.NotNull(retainedEarnings);
        Assert.Equal(ChartAccountTypes.Equity, retainedEarnings!.AccountType);
        Assert.Equal(NormalBalanceTypes.Credit, retainedEarnings.NormalBalanceType);

        var total = await _db.ChartAccounts.CountAsync(x => x.BusinessId == 1);
        Assert.Equal(AccountingDefaults.ChartOfAccounts.Count, total);
    }

    [Fact]
    public async Task EnsureInitializedAsync_CreatesBusinessConfigurationMappedToDefaultCodes()
    {
        var configuration = await CreateInitializationStore()
            .EnsureInitializedAsync(1, 9001, CancellationToken.None);

        Assert.NotNull(configuration.CashAccountId);
        Assert.NotNull(configuration.InventoryAccountId);
        Assert.NotNull(configuration.SalesAccountId);
        Assert.Equal("JR", configuration.JournalNumberPrefix);
        Assert.Equal(6, configuration.JournalNumberPadding);
        Assert.Equal(AccountingSequenceResetPolicies.PerFinancialYear, configuration.JournalSequenceResetPolicy);
        Assert.Equal(30, configuration.DefaultSupplierPaymentTermsDays);

        var byCode = await _db.ChartAccounts
            .Where(x => x.BusinessId == 1)
            .ToDictionaryAsync(x => x.AccountCode, x => x.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(byCode[AccountingDefaults.CashOnHandCode], configuration.CashAccountId);
        Assert.Equal(byCode[AccountingDefaults.InventoryCode], configuration.InventoryAccountId);
        Assert.Equal(byCode[AccountingDefaults.SalesCode], configuration.SalesAccountId);
        Assert.Equal(byCode[AccountingDefaults.RetainedEarningsCode], configuration.RetainedEarningsAccountId);
    }

    [Fact]
    public async Task EnsureInitializedAsync_ExistingConfiguration_MapsOnlyMissingRetainedEarningsAndPreservesOwnerMappings()
    {
        await SeedDefaultChartAccountsAsync();
        var ownerCapitalId = await _db.ChartAccounts.Where(x => x.BusinessId == 1 && x.AccountCode == AccountingDefaults.OwnerCapitalCode).Select(x => x.Id).SingleAsync();
        var ownerDrawingsId = await _db.ChartAccounts.Where(x => x.BusinessId == 1 && x.AccountCode == AccountingDefaults.OwnerDrawingsCode).Select(x => x.Id).SingleAsync();
        _db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            OwnerCapitalAccountId = ownerCapitalId,
            OwnerDrawingsAccountId = ownerDrawingsId,
            JournalNumberPrefix = "JR",
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear
        });
        await _db.SaveChangesAsync();

        var configuration = await CreateInitializationStore()
            .EnsureInitializedAsync(1, 9002, CancellationToken.None);

        Assert.NotNull(configuration.RetainedEarningsAccountId);
        Assert.Equal(ownerCapitalId, configuration.OwnerCapitalAccountId);
        Assert.Equal(ownerDrawingsId, configuration.OwnerDrawingsAccountId);
    }

    [Fact]
    public async Task UpdateBusinessConfigurationAsync_RejectsNonEquityRetainedEarningsAccount()
    {
        var configuration = await CreateInitializationStore()
            .EnsureInitializedAsync(1, 9001, CancellationToken.None);

        configuration.RetainedEarningsAccountId = configuration.CashAccountId;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateConfigurationWriteStore().UpdateBusinessConfigurationAsync(
                1, configuration, 9001, CancellationToken.None));
        Assert.Contains(PostingAccountSlots.RetainedEarnings, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureInitializedAsync_CreatesDefaultFinancialYearAndOpenPeriod_WhenMissing()
    {
        await CreateInitializationStore().EnsureInitializedAsync(1, 9001, CancellationToken.None);

        var financialYear = await _db.FinancialYears
            .SingleOrDefaultAsync(x => x.BusinessId == 1);

        Assert.NotNull(financialYear);
        Assert.True(financialYear!.IsCurrent);
        Assert.False(financialYear.IsClosed);

        var period = await _db.AccountingPeriods
            .SingleOrDefaultAsync(x => x.FinancialYearId == financialYear.Id);

        Assert.NotNull(period);
        Assert.Equal(AccountingPeriodStatuses.Open, period!.Status);
        Assert.True(period.StartDate <= period.EndDate);
    }

    [Fact]
    public async Task UpsertOutletConfigurationAsync_Throws_WhenOutletNotInBusiness()
    {
        _db.Businesses.Add(new Business { Id = 2, Name = "Other Biz", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _db.Outlets.Add(new Outlet { Id = 22, BusinessId = 2, Name = "Other Outlet", Address = "Y", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateOutletConfigurationWriteStore().UpsertAsync(
                1,
                new AccountingOutletConfiguration { OutletId = 22 },
                9001,
                CancellationToken.None));

        Assert.Contains("Outlet not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateCode_CaseInsensitive()
    {
        await CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "1100",
            AccountName = "Cash Box 1",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 10
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "1100",
            AccountName = "Cash Box 1 Duplicate",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 11
        }));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_RejectsHierarchyCycle()
    {
        var parent = await CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "1200",
            AccountName = "Parent Asset",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 20
        });

        var child = await CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "1201",
            AccountName = "Child Asset",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            ParentAccountId = parent.Id,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 21
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateChartAccountAsync(parent.Id, new UpdateChartAccountDto
        {
            AccountName = "Parent Asset",
            ParentAccountId = child.Id,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 20
        }));

        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_RejectsParentWithDifferentAccountType()
    {
        var parent = await CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "2000",
            AccountName = "Liability Parent",
            AccountType = ChartAccountTypes.Liability,
            NormalBalanceType = NormalBalanceTypes.Credit,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 30
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "1300",
            AccountName = "Asset Child",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            ParentAccountId = parent.Id,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 31
        }));

        Assert.Contains("same account type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeactivateAsync_RejectsWhenReferencedByJournalOrConfiguration()
    {
        var cash = await CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "1400",
            AccountName = "Cash Reference",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 40
        });

        _db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            CashAccountId = cash.Id,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var cfgEx = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateChartAccountAsync(cash.Id));
        Assert.Contains("configuration", cfgEx.Message, StringComparison.OrdinalIgnoreCase);

        var revenue = await CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "4100",
            AccountName = "Revenue Reference",
            AccountType = ChartAccountTypes.Income,
            NormalBalanceType = NormalBalanceTypes.Credit,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 41
        });

        _db.JournalEntries.Add(new JournalEntry
        {
            BusinessId = 1,
            JournalType = "ManualJournal",
            TransactionDate = DateTime.UtcNow,
            PostingDate = DateTime.UtcNow,
            BusinessDate = DateTime.UtcNow.Date,
            ReferenceType = "Manual",
            ReferenceId = "RM01-JRN-1",
            Status = JournalEntryStatuses.Posted,
            JournalNumber = "JR-2026-000001",
            IsSystemGenerated = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Lines =
            [
                new JournalEntryLine
                {
                    AccountId = revenue.Id,
                    DebitAmount = 0,
                    CreditAmount = 25,
                    CreatedAt = DateTime.UtcNow
                },
                new JournalEntryLine
                {
                    AccountId = cash.Id,
                    DebitAmount = 25,
                    CreditAmount = 0,
                    CreatedAt = DateTime.UtcNow
                }
            ]
        });

        await _db.SaveChangesAsync();

        var journalEx = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateChartAccountAsync(revenue.Id));
        Assert.Contains("journal", journalEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeactivateAsync_RejectsWhenAccountHasActiveChild()
    {
        var parent = await CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "1500",
            AccountName = "Parent",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            IsControlAccount = true,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 50
        });

        await CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "1501",
            AccountName = "Child",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            ParentAccountId = parent.Id,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 51
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateChartAccountAsync(parent.Id));
        Assert.Contains("active child", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateBusinessConfigurationAsync_RejectsInactiveAccountMapping()
    {
        var config = await CreateInitializationStore()
            .EnsureInitializedAsync(1, 9001, CancellationToken.None);
        var cash = await _db.ChartAccounts.FirstAsync(x => x.Id == config.CashAccountId);
        cash.IsActive = false;
        cash.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateConfigurationWriteStore().UpdateBusinessConfigurationAsync(
                1, config, 9001, CancellationToken.None));

        Assert.Contains("must be active", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateBusinessConfigurationAsync_RejectsTypeMismatchForSlot()
    {
        var config = await CreateInitializationStore()
            .EnsureInitializedAsync(1, 9001, CancellationToken.None);
        var incomeAccount = await CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "6998",
            AccountName = "Income Candidate",
            AccountType = ChartAccountTypes.Income,
            NormalBalanceType = NormalBalanceTypes.Credit,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 998
        });

        config.InventoryAccountId = incomeAccount.Id;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateConfigurationWriteStore().UpdateBusinessConfigurationAsync(
                1, config, 9001, CancellationToken.None));

        Assert.Contains("must be one of", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolvePostingAccountAsync_UsesOutletOverrideThenGlobalThenRejectsWhenMissing()
    {
        var tenant = CreateTenantAccessMock(1);
        var configService = RetailPOS.Tests.Infrastructure.AccountingConfigurationServiceTestFactory.Create(
            _db, tenant.Object, NullLogger<AccountingConfigurationService>.Instance);
        var config = await CreateInitializationStore()
            .EnsureInitializedAsync(1, 9001, CancellationToken.None);

        var overrideCash = await CreateChartAccountAsync(new CreateChartAccountDto
        {
            AccountCode = "1098",
            AccountName = "Outlet Cash Override",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            DisplayOrder = 998
        });

        await CreateOutletConfigurationWriteStore().UpsertAsync(
            1,
            new AccountingOutletConfiguration
            {
                OutletId = 11,
                OutletCashAccountId = overrideCash.Id,
                OutletPettyCashAccountId = config.PettyCashAccountId,
                OutletBankAccountId = config.DefaultBankAccountId
            },
            9001,
            CancellationToken.None);

        var fromOverride = await configService.ResolvePostingAccountAsync(PostingAccountSlots.Cash, 11);
        Assert.Equal(overrideCash.Id, fromOverride.AccountId);
        Assert.Equal("OutletOverride", fromOverride.Source);

        var fromGlobal = await configService.ResolvePostingAccountAsync(PostingAccountSlots.Inventory, 11);
        Assert.Equal(config.InventoryAccountId, fromGlobal.AccountId);
        Assert.Equal("GlobalConfiguration", fromGlobal.Source);

        var row = await _db.AccountingConfigurations.FirstAsync(x => x.BusinessId == 1);
        row.InventoryAccountId = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            configService.ResolvePostingAccountAsync(PostingAccountSlots.Inventory, 11));
    }

    private void SeedBusinessAndOutlet()
    {
        _db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Accounting Test Business",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Main Outlet",
            Address = "X",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    private static Mock<ITenantAccessService> CreateTenantAccessMock(long businessId)
    {
        var mock = new Mock<ITenantAccessService>();
        mock.Setup(x => x.IsSuperAdmin).Returns(false);
        mock.Setup(x => x.EffectiveBusinessId).Returns(businessId);
        mock.Setup(x => x.RequireBusinessId()).Returns(businessId);
        return mock;
    }

    private Task<RetailPOS.Application.ChartAccounts.ChartAccountReadModel> CreateChartAccountAsync(
        CreateChartAccountDto dto) =>
        new ChartAccountCommandStore(
                _db,
                null,
                NullLogger<ChartAccountCommandStore>.Instance)
            .CreateAsync(
                1,
                null,
                new CreateChartAccountCommand(
                    dto.AccountCode,
                    dto.AccountName,
                    dto.AccountType,
                    dto.NormalBalanceType,
                    dto.ParentAccountId,
                    dto.IsControlAccount,
                    dto.AllowManualPosting,
                    dto.IsActive,
                    dto.DisplayOrder,
                    null),
                CancellationToken.None);

    private Task<RetailPOS.Application.ChartAccounts.ChartAccountReadModel> UpdateChartAccountAsync(
        long id,
        UpdateChartAccountDto dto) =>
        new ChartAccountCommandStore(
                _db,
                null,
                NullLogger<ChartAccountCommandStore>.Instance)
            .UpdateAsync(
                1,
                null,
                new UpdateChartAccountCommand(
                    id,
                    dto.AccountName,
                    dto.ParentAccountId,
                    dto.IsControlAccount,
                    dto.AllowManualPosting,
                    dto.IsActive,
                    dto.DisplayOrder,
                    null),
                CancellationToken.None);

    private Task<bool> DeactivateChartAccountAsync(long id) =>
        new ChartAccountCommandStore(
                _db,
                null,
                NullLogger<ChartAccountCommandStore>.Instance)
            .DeactivateAsync(1, null, id, CancellationToken.None);

    private IAccountingConfigurationInitializationStore CreateInitializationStore() =>
        new AccountingConfigurationInitializationStore(
            new AccountingConfigurationProvisioner(
                _db,
                NullLogger<AccountingConfigurationProvisioner>.Instance));

    private IAccountingConfigurationWriteStore CreateConfigurationWriteStore() =>
        new AccountingConfigurationWriteStore(
            _db,
            null,
            NullLogger<AccountingConfigurationWriteStore>.Instance);

    private IAccountingOutletConfigurationWriteStore CreateOutletConfigurationWriteStore() =>
        new AccountingOutletConfigurationWriteStore(
            _db,
            new AccountingConfigurationProvisioner(
                _db,
                NullLogger<AccountingConfigurationProvisioner>.Instance),
            null);

    private async Task SeedDefaultChartAccountsAsync()
    {
        var now = DateTime.UtcNow;
        _db.ChartAccounts.AddRange(AccountingDefaults.ChartOfAccounts.Select(definition =>
            new ChartAccount
            {
                BusinessId = 1,
                AccountCode = definition.Code,
                AccountName = definition.Name,
                AccountType = definition.AccountType,
                NormalBalanceType = definition.NormalBalanceType,
                IsSystemAccount = true,
                IsControlAccount = definition.IsControlAccount,
                AllowManualPosting = definition.AllowManualPosting,
                IsActive = true,
                DisplayOrder = definition.DisplayOrder,
                CreatedAt = now,
                CreatedBy = 9001,
                UpdatedAt = now,
                UpdatedBy = 9001
            }));
        await _db.SaveChangesAsync();
    }
}
