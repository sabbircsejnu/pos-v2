using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.AccountingConfiguration.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingConfigurations;

public sealed class AccountingConfigurationWriteStore(
    RetailPOSDbContext dbContext,
    IAuditService? auditService,
    ILogger<AccountingConfigurationWriteStore> logger) : IAccountingConfigurationWriteStore
{
    public async Task<AccountingConfiguration> UpdateBusinessConfigurationAsync(
        long businessId,
        AccountingConfiguration incoming,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var configuration = await dbContext.AccountingConfigurations
            .FirstOrDefaultAsync(row => row.BusinessId == businessId, cancellationToken);
        var before = configuration is null ? null : Snapshot(configuration);
        var effective = MergeWithExistingConfiguration(incoming, configuration);
        await ValidateConfigurationAsync(businessId, effective, cancellationToken);

        if (configuration is null)
        {
            configuration = new AccountingConfiguration
            {
                BusinessId = businessId,
                CreatedAt = now,
                CreatedBy = actorUserId,
                UpdatedAt = now
            };
            dbContext.AccountingConfigurations.Add(configuration);
        }

        ApplyConfiguration(configuration, effective);
        configuration.UpdatedAt = now;
        configuration.UpdatedBy = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Accounting configuration updated for business {BusinessId}",
            businessId);
        await RecordConfigurationAuditAsync(
            configuration.Id,
            businessId,
            before is null ? AuditOperationType.Insert : AuditOperationType.Update,
            before,
            Snapshot(configuration),
            actorUserId,
            cancellationToken);
        return configuration;
    }

    private async Task ValidateConfigurationAsync(
        long businessId,
        AccountingConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var mappings = PostingAccountResolutionPolicy.GetGlobalAccountMappings(configuration);
        var accountIds = mappings
            .Where(mapping => mapping.AccountId.HasValue)
            .Select(mapping => mapping.AccountId!.Value)
            .Distinct()
            .ToList();
        if (accountIds.Count == 0)
        {
            return;
        }

        var accounts = await dbContext.ChartAccounts
            .AsNoTracking()
            .Where(account =>
                account.BusinessId == businessId &&
                accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, cancellationToken);

        var missingIds = accountIds.Where(id => !accounts.ContainsKey(id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new InvalidOperationException($"Invalid chart account IDs: {string.Join(", ", missingIds)}");
        }

        foreach (var mapping in mappings.Where(mapping => mapping.AccountId.HasValue))
        {
            var account = accounts[mapping.AccountId!.Value];
            if (!account.IsActive)
            {
                throw new InvalidOperationException(
                    $"Mapped account {account.Id} for slot '{mapping.Slot}' must be active.");
            }

            var allowedTypes = PostingAccountResolutionPolicy.GetAllowedAccountTypes(mapping.Slot);
            if (!allowedTypes.Any(type =>
                    string.Equals(type, account.AccountType, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Mapped account {account.Id} for slot '{mapping.Slot}' must be one of: {string.Join(", ", allowedTypes)}.");
            }
        }
    }

    private async Task RecordConfigurationAuditAsync(
        long configurationId,
        long businessId,
        string operationType,
        IReadOnlyDictionary<string, object?>? before,
        IReadOnlyDictionary<string, object?> after,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        if (auditService is null)
        {
            return;
        }

        var changes = after
            .Select(field => (
                FieldName: field.Key,
                OldValue: before is null ? null : before[field.Key],
                NewValue: field.Value))
            .Where(change => !Equals(change.OldValue, change.NewValue))
            .ToList();

        await auditService.RecordAsync(new AuditEventInput
        {
            ActionType = AuditActionType.Update,
            Module = AuditModule.Accounts,
            Summary = $"Accounting configuration {(operationType == AuditOperationType.Insert ? "created" : "updated")} for business {businessId}",
            PrimaryEntity = ("AccountingConfiguration", configurationId.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = businessId,
                ["actorUserId"] = actorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "AccountingConfiguration",
                    configurationId.ToString(),
                    operationType,
                    changes)
            ]
        }, cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?> Snapshot(AccountingConfiguration configuration) =>
        new Dictionary<string, object?>
        {
            [nameof(configuration.CashAccountId)] = configuration.CashAccountId,
            [nameof(configuration.PettyCashAccountId)] = configuration.PettyCashAccountId,
            [nameof(configuration.DefaultBankAccountId)] = configuration.DefaultBankAccountId,
            [nameof(configuration.InventoryAccountId)] = configuration.InventoryAccountId,
            [nameof(configuration.SalesAccountId)] = configuration.SalesAccountId,
            [nameof(configuration.SalesDiscountAccountId)] = configuration.SalesDiscountAccountId,
            [nameof(configuration.SalesReturnAccountId)] = configuration.SalesReturnAccountId,
            [nameof(configuration.CogsAccountId)] = configuration.CogsAccountId,
            [nameof(configuration.InventoryShortageAccountId)] = configuration.InventoryShortageAccountId,
            [nameof(configuration.InventoryGainAccountId)] = configuration.InventoryGainAccountId,
            [nameof(configuration.SupplierPayableAccountId)] = configuration.SupplierPayableAccountId,
            [nameof(configuration.SupplierAdvanceAccountId)] = configuration.SupplierAdvanceAccountId,
            [nameof(configuration.CustomerAdvanceAccountId)] = configuration.CustomerAdvanceAccountId,
            [nameof(configuration.OtherIncomeAccountId)] = configuration.OtherIncomeAccountId,
            [nameof(configuration.OwnerCapitalAccountId)] = configuration.OwnerCapitalAccountId,
            [nameof(configuration.OwnerDrawingsAccountId)] = configuration.OwnerDrawingsAccountId,
            [nameof(configuration.RetainedEarningsAccountId)] = configuration.RetainedEarningsAccountId,
            [nameof(configuration.RoundingDifferenceAccountId)] = configuration.RoundingDifferenceAccountId,
            [nameof(configuration.CashDifferenceGainAccountId)] = configuration.CashDifferenceGainAccountId,
            [nameof(configuration.CashDifferenceLossAccountId)] = configuration.CashDifferenceLossAccountId,
            [nameof(configuration.VatPayableAccountId)] = configuration.VatPayableAccountId,
            [nameof(configuration.VatReceivableAccountId)] = configuration.VatReceivableAccountId,
            [nameof(configuration.CardClearingAccountId)] = configuration.CardClearingAccountId,
            [nameof(configuration.MobileMfsClearingAccountId)] = configuration.MobileMfsClearingAccountId,
            [nameof(configuration.SettlementFeeExpenseAccountId)] = configuration.SettlementFeeExpenseAccountId,
            [nameof(configuration.BankFeeExpenseAccountId)] = configuration.BankFeeExpenseAccountId,
            [nameof(configuration.BankInterestIncomeAccountId)] = configuration.BankInterestIncomeAccountId,
            [nameof(configuration.BankClearingActivationDate)] = configuration.BankClearingActivationDate,
            [nameof(configuration.DefaultSupplierPaymentTermsDays)] = configuration.DefaultSupplierPaymentTermsDays,
            [nameof(configuration.JournalNumberPrefix)] = configuration.JournalNumberPrefix,
            [nameof(configuration.JournalNumberPadding)] = configuration.JournalNumberPadding,
            [nameof(configuration.JournalSequenceResetPolicy)] = configuration.JournalSequenceResetPolicy,
            [nameof(configuration.ManualJournalWorkflowMode)] = configuration.ManualJournalWorkflowMode,
            [nameof(configuration.ManualJournalAllowSelfApproval)] = configuration.ManualJournalAllowSelfApproval,
            [nameof(configuration.ManualJournalAttachmentMaxBytes)] = configuration.ManualJournalAttachmentMaxBytes
        };

    private static AccountingConfiguration MergeWithExistingConfiguration(
        AccountingConfiguration incoming,
        AccountingConfiguration? existing)
    {
        if (existing is null)
        {
            return incoming;
        }

        return new AccountingConfiguration
        {
            CashAccountId = incoming.CashAccountId ?? existing.CashAccountId,
            PettyCashAccountId = incoming.PettyCashAccountId ?? existing.PettyCashAccountId,
            DefaultBankAccountId = incoming.DefaultBankAccountId ?? existing.DefaultBankAccountId,
            InventoryAccountId = incoming.InventoryAccountId ?? existing.InventoryAccountId,
            SalesAccountId = incoming.SalesAccountId ?? existing.SalesAccountId,
            SalesDiscountAccountId = incoming.SalesDiscountAccountId ?? existing.SalesDiscountAccountId,
            SalesReturnAccountId = incoming.SalesReturnAccountId ?? existing.SalesReturnAccountId,
            CogsAccountId = incoming.CogsAccountId ?? existing.CogsAccountId,
            InventoryShortageAccountId = incoming.InventoryShortageAccountId ?? existing.InventoryShortageAccountId,
            InventoryGainAccountId = incoming.InventoryGainAccountId ?? existing.InventoryGainAccountId,
            SupplierPayableAccountId = incoming.SupplierPayableAccountId ?? existing.SupplierPayableAccountId,
            SupplierAdvanceAccountId = incoming.SupplierAdvanceAccountId ?? existing.SupplierAdvanceAccountId,
            CustomerAdvanceAccountId = incoming.CustomerAdvanceAccountId ?? existing.CustomerAdvanceAccountId,
            OtherIncomeAccountId = incoming.OtherIncomeAccountId ?? existing.OtherIncomeAccountId,
            OwnerCapitalAccountId = incoming.OwnerCapitalAccountId ?? existing.OwnerCapitalAccountId,
            OwnerDrawingsAccountId = incoming.OwnerDrawingsAccountId ?? existing.OwnerDrawingsAccountId,
            RetainedEarningsAccountId = incoming.RetainedEarningsAccountId ?? existing.RetainedEarningsAccountId,
            RoundingDifferenceAccountId = incoming.RoundingDifferenceAccountId ?? existing.RoundingDifferenceAccountId,
            CashDifferenceGainAccountId = incoming.CashDifferenceGainAccountId ?? existing.CashDifferenceGainAccountId,
            CashDifferenceLossAccountId = incoming.CashDifferenceLossAccountId ?? existing.CashDifferenceLossAccountId,
            VatPayableAccountId = incoming.VatPayableAccountId ?? existing.VatPayableAccountId,
            VatReceivableAccountId = incoming.VatReceivableAccountId ?? existing.VatReceivableAccountId,
            CardClearingAccountId = incoming.CardClearingAccountId ?? existing.CardClearingAccountId,
            MobileMfsClearingAccountId = incoming.MobileMfsClearingAccountId ?? existing.MobileMfsClearingAccountId,
            SettlementFeeExpenseAccountId = incoming.SettlementFeeExpenseAccountId ?? existing.SettlementFeeExpenseAccountId,
            BankFeeExpenseAccountId = incoming.BankFeeExpenseAccountId ?? existing.BankFeeExpenseAccountId,
            BankInterestIncomeAccountId = incoming.BankInterestIncomeAccountId ?? existing.BankInterestIncomeAccountId,
            BankClearingActivationDate = incoming.BankClearingActivationDate ?? existing.BankClearingActivationDate,
            DefaultSupplierPaymentTermsDays = incoming.DefaultSupplierPaymentTermsDays == 0
                ? existing.DefaultSupplierPaymentTermsDays
                : incoming.DefaultSupplierPaymentTermsDays,
            JournalNumberPrefix = string.IsNullOrWhiteSpace(incoming.JournalNumberPrefix)
                ? existing.JournalNumberPrefix
                : incoming.JournalNumberPrefix,
            JournalNumberPadding = incoming.JournalNumberPadding == 0
                ? existing.JournalNumberPadding
                : incoming.JournalNumberPadding,
            JournalSequenceResetPolicy = string.IsNullOrWhiteSpace(incoming.JournalSequenceResetPolicy)
                ? existing.JournalSequenceResetPolicy
                : incoming.JournalSequenceResetPolicy,
            ManualJournalWorkflowMode = string.IsNullOrWhiteSpace(incoming.ManualJournalWorkflowMode)
                ? existing.ManualJournalWorkflowMode
                : incoming.ManualJournalWorkflowMode,
            ManualJournalAllowSelfApproval =
                incoming.ManualJournalAllowSelfApproval || existing.ManualJournalAllowSelfApproval,
            ManualJournalAttachmentMaxBytes = incoming.ManualJournalAttachmentMaxBytes == 0
                ? existing.ManualJournalAttachmentMaxBytes
                : incoming.ManualJournalAttachmentMaxBytes
        };
    }

    private static void ApplyConfiguration(
        AccountingConfiguration row,
        AccountingConfiguration configuration)
    {
        row.CashAccountId = configuration.CashAccountId;
        row.PettyCashAccountId = configuration.PettyCashAccountId;
        row.DefaultBankAccountId = configuration.DefaultBankAccountId;
        row.InventoryAccountId = configuration.InventoryAccountId;
        row.SalesAccountId = configuration.SalesAccountId;
        row.SalesDiscountAccountId = configuration.SalesDiscountAccountId;
        row.SalesReturnAccountId = configuration.SalesReturnAccountId;
        row.CogsAccountId = configuration.CogsAccountId;
        row.InventoryShortageAccountId = configuration.InventoryShortageAccountId;
        row.InventoryGainAccountId = configuration.InventoryGainAccountId;
        row.SupplierPayableAccountId = configuration.SupplierPayableAccountId;
        row.SupplierAdvanceAccountId = configuration.SupplierAdvanceAccountId;
        row.CustomerAdvanceAccountId = configuration.CustomerAdvanceAccountId;
        row.OtherIncomeAccountId = configuration.OtherIncomeAccountId;
        row.OwnerCapitalAccountId = configuration.OwnerCapitalAccountId;
        row.OwnerDrawingsAccountId = configuration.OwnerDrawingsAccountId;
        row.RetainedEarningsAccountId = configuration.RetainedEarningsAccountId;
        row.RoundingDifferenceAccountId = configuration.RoundingDifferenceAccountId;
        row.CashDifferenceGainAccountId = configuration.CashDifferenceGainAccountId;
        row.CashDifferenceLossAccountId = configuration.CashDifferenceLossAccountId;
        row.VatPayableAccountId = configuration.VatPayableAccountId;
        row.VatReceivableAccountId = configuration.VatReceivableAccountId;
        row.CardClearingAccountId = configuration.CardClearingAccountId;
        row.MobileMfsClearingAccountId = configuration.MobileMfsClearingAccountId;
        row.SettlementFeeExpenseAccountId = configuration.SettlementFeeExpenseAccountId;
        row.BankFeeExpenseAccountId = configuration.BankFeeExpenseAccountId;
        row.BankInterestIncomeAccountId = configuration.BankInterestIncomeAccountId;
        row.BankClearingActivationDate = configuration.BankClearingActivationDate?.Date;
        row.DefaultSupplierPaymentTermsDays = Math.Clamp(
            configuration.DefaultSupplierPaymentTermsDays,
            0,
            3650);
        row.JournalNumberPrefix = string.IsNullOrWhiteSpace(configuration.JournalNumberPrefix)
            ? "JR"
            : configuration.JournalNumberPrefix.Trim().ToUpperInvariant();
        row.JournalNumberPadding = Math.Clamp(configuration.JournalNumberPadding, 3, 12);
        row.JournalSequenceResetPolicy = string.Equals(
            configuration.JournalSequenceResetPolicy,
            AccountingSequenceResetPolicies.Continuous,
            StringComparison.OrdinalIgnoreCase)
                ? AccountingSequenceResetPolicies.Continuous
                : AccountingSequenceResetPolicies.PerFinancialYear;
        row.ManualJournalWorkflowMode = string.Equals(
            configuration.ManualJournalWorkflowMode,
            ManualJournalWorkflowModes.ApprovalRequired,
            StringComparison.OrdinalIgnoreCase)
                ? ManualJournalWorkflowModes.ApprovalRequired
                : ManualJournalWorkflowModes.DraftPost;
        row.ManualJournalAllowSelfApproval = configuration.ManualJournalAllowSelfApproval;
        row.ManualJournalAttachmentMaxBytes = Math.Clamp(
            configuration.ManualJournalAttachmentMaxBytes,
            1_048_576,
            100L * 1024 * 1024);
    }
}
