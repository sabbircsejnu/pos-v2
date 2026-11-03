using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingConfigurations;

public sealed class AccountingConfigurationProvisioner(
    RetailPOSDbContext dbContext,
    ILogger<AccountingConfigurationProvisioner> logger)
{
    public async Task<AccountingConfiguration> EnsureInitializedAsync(
        long businessId,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        await InitializeChartAccountsAsync(businessId, actorUserId, cancellationToken);

        var existing = await dbContext.AccountingConfigurations
            .FirstOrDefaultAsync(configuration => configuration.BusinessId == businessId, cancellationToken);
        if (existing is not null)
        {
            if (!existing.RetainedEarningsAccountId.HasValue)
            {
                var retainedEarningsId = await dbContext.ChartAccounts
                    .Where(account =>
                        account.BusinessId == businessId &&
                        account.AccountCode == AccountingDefaults.RetainedEarningsCode)
                    .Select(account => (long?)account.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                if (retainedEarningsId.HasValue)
                {
                    existing.RetainedEarningsAccountId = retainedEarningsId;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.UpdatedBy = actorUserId;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            return existing;
        }

        var accountByCode = await dbContext.ChartAccounts
            .Where(account => account.BusinessId == businessId)
            .ToDictionaryAsync(
                account => account.AccountCode,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);
        var now = DateTime.UtcNow;
        var created = new AccountingConfiguration
        {
            BusinessId = businessId,
            CashAccountId = accountByCode[AccountingDefaults.CashOnHandCode].Id,
            PettyCashAccountId = accountByCode[AccountingDefaults.PettyCashCode].Id,
            DefaultBankAccountId = accountByCode[AccountingDefaults.BankCode].Id,
            InventoryAccountId = accountByCode[AccountingDefaults.InventoryCode].Id,
            SalesAccountId = accountByCode[AccountingDefaults.SalesCode].Id,
            SalesDiscountAccountId = accountByCode[AccountingDefaults.SalesDiscountCode].Id,
            SalesReturnAccountId = accountByCode[AccountingDefaults.SalesReturnCode].Id,
            CogsAccountId = accountByCode[AccountingDefaults.CogsCode].Id,
            InventoryShortageAccountId = accountByCode[AccountingDefaults.InventoryShortageCode].Id,
            InventoryGainAccountId = accountByCode[AccountingDefaults.InventoryGainCode].Id,
            SupplierPayableAccountId = accountByCode[AccountingDefaults.SupplierPayableCode].Id,
            SupplierAdvanceAccountId = accountByCode[AccountingDefaults.SupplierAdvanceCode].Id,
            CustomerAdvanceAccountId = accountByCode[AccountingDefaults.CustomerAdvanceCode].Id,
            OtherIncomeAccountId = accountByCode[AccountingDefaults.OtherIncomeCode].Id,
            OwnerCapitalAccountId = accountByCode[AccountingDefaults.OwnerCapitalCode].Id,
            OwnerDrawingsAccountId = accountByCode[AccountingDefaults.OwnerDrawingsCode].Id,
            RetainedEarningsAccountId = accountByCode[AccountingDefaults.RetainedEarningsCode].Id,
            RoundingDifferenceAccountId = accountByCode[AccountingDefaults.RoundingDifferenceCode].Id,
            CashDifferenceGainAccountId = accountByCode[AccountingDefaults.CashDifferenceGainCode].Id,
            CashDifferenceLossAccountId = accountByCode[AccountingDefaults.CashDifferenceLossCode].Id,
            VatPayableAccountId = accountByCode[AccountingDefaults.SupplierPayableCode].Id,
            VatReceivableAccountId = accountByCode[AccountingDefaults.VatReceivableCode].Id,
            CardClearingAccountId = accountByCode[AccountingDefaults.CardClearingCode].Id,
            MobileMfsClearingAccountId = accountByCode[AccountingDefaults.MobileMfsClearingCode].Id,
            SettlementFeeExpenseAccountId = accountByCode[AccountingDefaults.SettlementFeeExpenseCode].Id,
            BankFeeExpenseAccountId = accountByCode[AccountingDefaults.BankFeeExpenseCode].Id,
            BankInterestIncomeAccountId = accountByCode[AccountingDefaults.BankInterestIncomeCode].Id,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId
        };
        dbContext.AccountingConfigurations.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken);

        await EnsureDefaultFinancialYearAndPeriodAsync(businessId, actorUserId, cancellationToken);
        logger.LogInformation("Accounting configuration initialized for business {BusinessId}", businessId);
        return created;
    }

    private async Task InitializeChartAccountsAsync(
        long businessId,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        var existingCodes = await dbContext.ChartAccounts
            .Where(account => account.BusinessId == businessId)
            .Select(account => account.AccountCode)
            .ToListAsync(cancellationToken);
        var existing = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        foreach (var definition in AccountingDefaults.ChartOfAccounts)
        {
            if (existing.Contains(definition.Code))
                continue;

            dbContext.ChartAccounts.Add(new ChartAccount
            {
                BusinessId = businessId,
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
                CreatedBy = actorUserId,
                UpdatedAt = now,
                UpdatedBy = actorUserId
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureDefaultFinancialYearAndPeriodAsync(
        long businessId,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        var existingYear = await dbContext.FinancialYears
            .AsNoTracking()
            .FirstOrDefaultAsync(year => year.BusinessId == businessId, cancellationToken);
        var now = DateTime.UtcNow;

        if (existingYear is not null)
        {
            if (await dbContext.AccountingPeriods.AnyAsync(
                    period => period.FinancialYearId == existingYear.Id,
                    cancellationToken))
                return;

            dbContext.AccountingPeriods.Add(new AccountingPeriod
            {
                FinancialYearId = existingYear.Id,
                PeriodNumber = 1,
                Name = "Period 1",
                StartDate = existingYear.StartDate,
                EndDate = existingYear.EndDate,
                Status = AccountingPeriodStatuses.Open,
                CreatedAt = now,
                CreatedBy = actorUserId,
                UpdatedAt = now,
                UpdatedBy = actorUserId
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var year = new FinancialYear
        {
            BusinessId = businessId,
            YearCode = $"FY{now:yyyy}",
            Name = $"FY {now:yyyy}",
            StartDate = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(now.Year, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            IsCurrent = true,
            IsClosed = false,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId
        };
        dbContext.FinancialYears.Add(year);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.AccountingPeriods.Add(new AccountingPeriod
        {
            FinancialYearId = year.Id,
            PeriodNumber = 1,
            Name = "Period 1",
            StartDate = year.StartDate,
            EndDate = year.EndDate,
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
