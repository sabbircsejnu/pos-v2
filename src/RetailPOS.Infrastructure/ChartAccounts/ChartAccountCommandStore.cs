using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.ChartAccounts;
using RetailPOS.Application.ChartAccounts.Abstractions;
using RetailPOS.Application.ChartAccounts.Commands;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ChartAccounts;

public sealed class ChartAccountCommandStore(
    RetailPOSDbContext dbContext,
    IAuditService? auditService,
    ILogger<ChartAccountCommandStore> logger)
    : IChartAccountCommandStore
{
    private static readonly HashSet<string> SupportedAccountTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ChartAccountTypes.Asset,
        ChartAccountTypes.Liability,
        ChartAccountTypes.Equity,
        ChartAccountTypes.Income,
        ChartAccountTypes.CostOfGoodsSold,
        ChartAccountTypes.Expense
    };

    private static readonly HashSet<string> SupportedNormalBalances = new(StringComparer.OrdinalIgnoreCase)
    {
        NormalBalanceTypes.Debit,
        NormalBalanceTypes.Credit
    };

    public async Task<ChartAccountReadModel> CreateAsync(
        long businessId,
        long? actorUserId,
        CreateChartAccountCommand command,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var accountCode = NormalizeRequired(command.AccountCode, nameof(command.AccountCode), 30).ToUpperInvariant();
        var accountName = NormalizeRequired(command.AccountName, nameof(command.AccountName), 150);
        var accountType = NormalizeAccountType(command.AccountType);
        var normalBalanceType = NormalizeNormalBalanceType(command.NormalBalanceType);

        await EnsureUniqueCodeAsync(businessId, accountCode, cancellationToken);
        await EnsureValidParentAsync(businessId, command.ParentAccountId, accountType, cancellationToken);

        var account = new ChartAccount
        {
            BusinessId = businessId,
            ParentAccountId = command.ParentAccountId,
            AccountCode = accountCode,
            AccountName = accountName,
            AccountType = accountType,
            NormalBalanceType = normalBalanceType,
            IsSystemAccount = false,
            IsControlAccount = command.IsControlAccount,
            AllowManualPosting = command.AllowManualPosting,
            IsActive = command.IsActive,
            DisplayOrder = command.DisplayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ChartAccounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Chart account {AccountCode} created for business {BusinessId}",
            account.AccountCode,
            businessId);
        var after = Map(account);
        await RecordAuditAsync(
            AuditActionType.Create,
            $"Chart account created: {account.Id}",
            account,
            AuditOperationType.Insert,
            null,
            after,
            actorUserId);
        return after;
    }

    public async Task<ChartAccountReadModel> UpdateAsync(
        long businessId,
        long? actorUserId,
        UpdateChartAccountCommand command,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.ChartAccounts.FirstOrDefaultAsync(
                row => row.Id == command.Id && row.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Chart account with ID {command.Id} was not found.");
        var before = Map(account);
        var normalizedName = NormalizeRequired(command.AccountName, nameof(command.AccountName), 150);

        if (account.IsSystemAccount && !command.IsActive)
        {
            throw new InvalidOperationException("System accounts cannot be deactivated.");
        }

        if (account.IsSystemAccount)
        {
            if (command.ParentAccountId != account.ParentAccountId)
            {
                throw new InvalidOperationException("System accounts cannot be re-parented.");
            }

            if (command.IsControlAccount != account.IsControlAccount)
            {
                throw new InvalidOperationException("System account control behavior cannot be changed.");
            }

            if (command.AllowManualPosting != account.AllowManualPosting)
            {
                throw new InvalidOperationException("System account manual-posting behavior cannot be changed.");
            }
        }

        if (command.ParentAccountId == command.Id)
        {
            throw new InvalidOperationException("Account cannot be parent of itself.");
        }

        await EnsureValidParentAsync(
            businessId,
            command.ParentAccountId,
            account.AccountType,
            cancellationToken);
        await EnsureNoHierarchyCycleAsync(
            businessId,
            command.Id,
            command.ParentAccountId,
            cancellationToken);

        account.AccountName = normalizedName;
        account.ParentAccountId = command.ParentAccountId;
        account.IsControlAccount = command.IsControlAccount;
        account.AllowManualPosting = command.AllowManualPosting;
        account.IsActive = command.IsActive;
        account.DisplayOrder = command.DisplayOrder;
        account.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Chart account {AccountId} updated for business {BusinessId}",
            command.Id,
            businessId);
        var after = Map(account);
        await RecordAuditAsync(
            AuditActionType.Update,
            $"Chart account updated: {command.Id}",
            account,
            AuditOperationType.Update,
            before,
            after,
            actorUserId);
        return after;
    }

    public async Task<bool> DeactivateAsync(
        long businessId,
        long? actorUserId,
        long accountId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.ChartAccounts.FirstOrDefaultAsync(
                row => row.Id == accountId && row.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Chart account with ID {accountId} was not found.");
        var before = Map(account);

        if (account.IsSystemAccount)
        {
            throw new InvalidOperationException("System accounts cannot be deactivated.");
        }

        if (!account.IsActive)
        {
            return false;
        }

        if (await HasActiveChildrenAsync(businessId, accountId, cancellationToken))
        {
            throw new InvalidOperationException("Account cannot be deactivated while it has active child accounts.");
        }

        if (await IsReferencedByJournalAsync(accountId, cancellationToken))
        {
            throw new InvalidOperationException("Account cannot be deactivated because it is referenced by journal transactions.");
        }

        if (await IsReferencedByConfigurationAsync(businessId, accountId, cancellationToken))
        {
            throw new InvalidOperationException("Account cannot be deactivated because it is used in accounting configuration.");
        }

        account.IsActive = false;
        account.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Chart account {AccountId} deactivated for business {BusinessId}",
            accountId,
            businessId);
        await RecordAuditAsync(
            AuditActionType.Update,
            $"Chart account deactivated: {accountId}",
            account,
            AuditOperationType.Update,
            before,
            Map(account),
            actorUserId);
        return true;
    }

    private async Task RecordAuditAsync(
        string actionType,
        string summary,
        ChartAccount account,
        string operationType,
        ChartAccountReadModel? before,
        ChartAccountReadModel after,
        long? actorUserId)
    {
        if (auditService is null)
        {
            return;
        }

        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = actionType,
            Module = AuditModule.Accounts,
            Summary = summary,
            PrimaryEntity = ("ChartAccount", account.Id.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = account.BusinessId,
                ["accountCode"] = account.AccountCode,
                ["actorUserId"] = actorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "ChartAccount",
                    account.Id.ToString(),
                    operationType,
                    BuildFieldChanges(before, after))
            ]
        });
    }

    private static List<(string FieldName, object? OldValue, object? NewValue)> BuildFieldChanges(
        ChartAccountReadModel? before,
        ChartAccountReadModel after) =>
        typeof(ChartAccountReadModel)
            .GetProperties()
            .Select(property =>
                (property.Name,
                    OldValue: before is null ? null : property.GetValue(before),
                    NewValue: property.GetValue(after)))
            .Where(change => !Equals(change.OldValue, change.NewValue))
            .Select(change => (change.Name, change.OldValue, change.NewValue))
            .ToList();

    private async Task EnsureUniqueCodeAsync(
        long businessId,
        string accountCode,
        CancellationToken cancellationToken)
    {
        var normalizedCode = accountCode.Trim().ToUpperInvariant();
        var exists = await dbContext.ChartAccounts.AnyAsync(
            account => account.BusinessId == businessId
                && account.AccountCode.ToUpper() == normalizedCode,
            cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException($"An account with code '{accountCode}' already exists.");
        }
    }

    private async Task EnsureValidParentAsync(
        long businessId,
        long? parentAccountId,
        string childAccountType,
        CancellationToken cancellationToken)
    {
        if (!parentAccountId.HasValue)
        {
            return;
        }

        var parent = await dbContext.ChartAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                account => account.Id == parentAccountId.Value && account.BusinessId == businessId,
                cancellationToken);

        if (parent is null)
        {
            throw new InvalidOperationException("Parent account was not found in this business.");
        }

        if (!parent.IsActive)
        {
            throw new InvalidOperationException("Parent account must be active.");
        }

        if (!string.Equals(parent.AccountType, childAccountType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Parent and child accounts must belong to the same account type.");
        }
    }

    private async Task EnsureNoHierarchyCycleAsync(
        long businessId,
        long accountId,
        long? parentAccountId,
        CancellationToken cancellationToken)
    {
        var currentParentId = parentAccountId;
        while (currentParentId.HasValue)
        {
            if (currentParentId.Value == accountId)
            {
                throw new InvalidOperationException("Parent assignment would create a hierarchy cycle.");
            }

            currentParentId = await dbContext.ChartAccounts
                .Where(account =>
                    account.BusinessId == businessId && account.Id == currentParentId.Value)
                .Select(account => account.ParentAccountId)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }

    private Task<bool> HasActiveChildrenAsync(
        long businessId,
        long accountId,
        CancellationToken cancellationToken) =>
        dbContext.ChartAccounts.AnyAsync(
            account => account.BusinessId == businessId
                && account.ParentAccountId == accountId
                && account.IsActive,
            cancellationToken);

    private Task<bool> IsReferencedByJournalAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        dbContext.JournalEntryLines.AnyAsync(
            line => line.AccountId == accountId,
            cancellationToken);

    private async Task<bool> IsReferencedByConfigurationAsync(
        long businessId,
        long accountId,
        CancellationToken cancellationToken)
    {
        var businessReferenced = await dbContext.AccountingConfigurations.AnyAsync(configuration =>
            configuration.BusinessId == businessId
            && (configuration.CashAccountId == accountId
                || configuration.PettyCashAccountId == accountId
                || configuration.DefaultBankAccountId == accountId
                || configuration.InventoryAccountId == accountId
                || configuration.SalesAccountId == accountId
                || configuration.SalesDiscountAccountId == accountId
                || configuration.SalesReturnAccountId == accountId
                || configuration.CogsAccountId == accountId
                || configuration.InventoryShortageAccountId == accountId
                || configuration.InventoryGainAccountId == accountId
                || configuration.SupplierPayableAccountId == accountId
                || configuration.CustomerAdvanceAccountId == accountId
                || configuration.OtherIncomeAccountId == accountId
                || configuration.OwnerCapitalAccountId == accountId
                || configuration.OwnerDrawingsAccountId == accountId
                || configuration.RetainedEarningsAccountId == accountId
                || configuration.RoundingDifferenceAccountId == accountId
                || configuration.CashDifferenceGainAccountId == accountId
                || configuration.CashDifferenceLossAccountId == accountId),
            cancellationToken);

        if (businessReferenced)
        {
            return true;
        }

        return await dbContext.AccountingOutletConfigurations.AnyAsync(configuration =>
                configuration.BusinessId == businessId
                && (configuration.OutletCashAccountId == accountId
                    || configuration.OutletPettyCashAccountId == accountId
                    || configuration.OutletBankAccountId == accountId),
            cancellationToken);
    }

    private static string NormalizeRequired(string value, string fieldName, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new InvalidOperationException($"{fieldName} exceeds maximum length of {maxLength}.");
        }

        return trimmed;
    }

    private static string NormalizeAccountType(string accountType)
    {
        var normalized = accountType.Trim();
        if (!SupportedAccountTypes.Contains(normalized))
        {
            throw new InvalidOperationException("Unsupported account type.");
        }

        return SupportedAccountTypes.First(type =>
            type.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeNormalBalanceType(string normalBalanceType)
    {
        var normalized = normalBalanceType.Trim();
        if (!SupportedNormalBalances.Contains(normalized))
        {
            throw new InvalidOperationException("Unsupported normal balance type.");
        }

        return SupportedNormalBalances.First(type =>
            type.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static ChartAccountReadModel Map(ChartAccount account) =>
        new(
            account.Id,
            account.AccountCode,
            account.AccountName,
            account.AccountType,
            account.NormalBalanceType,
            account.ParentAccountId,
            account.IsSystemAccount,
            account.IsControlAccount,
            account.AllowManualPosting,
            account.IsActive,
            account.DisplayOrder);
}
