using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.AccountingConfiguration.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingConfigurations;

public sealed class AccountingOutletConfigurationWriteStore(
    RetailPOSDbContext dbContext,
    AccountingConfigurationProvisioner provisioner,
    IAuditService? auditService) : IAccountingOutletConfigurationWriteStore
{
    public async Task<AccountingOutletConfiguration> UpsertAsync(
        long businessId,
        AccountingOutletConfiguration configuration,
        long? actorUserId,
        CancellationToken cancellationToken)
    {
        var outletExists = await dbContext.Outlets.AnyAsync(
            outlet => outlet.Id == configuration.OutletId && outlet.BusinessId == businessId,
            cancellationToken);
        if (!outletExists)
        {
            throw new InvalidOperationException("Outlet not found in this business.");
        }

        await provisioner.EnsureInitializedAsync(businessId, actorUserId, cancellationToken);

        var row = await dbContext.AccountingOutletConfigurations
            .FirstOrDefaultAsync(
                existing =>
                    existing.BusinessId == businessId &&
                    existing.OutletId == configuration.OutletId,
                cancellationToken);
        var before = row is null ? null : Snapshot(row);
        await ValidateConfigurationAsync(businessId, configuration, cancellationToken);

        var now = DateTime.UtcNow;
        if (row is null)
        {
            row = new AccountingOutletConfiguration
            {
                BusinessId = businessId,
                OutletId = configuration.OutletId,
                CreatedAt = now,
                CreatedBy = actorUserId
            };
            dbContext.AccountingOutletConfigurations.Add(row);
        }

        row.OutletCashAccountId = configuration.OutletCashAccountId;
        row.OutletPettyCashAccountId = configuration.OutletPettyCashAccountId;
        row.OutletBankAccountId = configuration.OutletBankAccountId;
        row.UpdatedAt = now;
        row.UpdatedBy = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync(
            row.Id,
            businessId,
            row.OutletId,
            before is null ? AuditOperationType.Insert : AuditOperationType.Update,
            before,
            Snapshot(row),
            actorUserId,
            cancellationToken);
        return row;
    }

    private async Task ValidateConfigurationAsync(
        long businessId,
        AccountingOutletConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var mappings = PostingAccountResolutionPolicy.GetOutletAccountMappings(configuration);
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

    private async Task RecordAuditAsync(
        long configurationId,
        long businessId,
        long outletId,
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
            Summary = $"Outlet accounting configuration {(operationType == AuditOperationType.Insert ? "created" : "updated")}: {outletId}",
            PrimaryEntity = ("AccountingOutletConfiguration", configurationId.ToString()),
            Metadata = new Dictionary<string, object?>
            {
                ["businessId"] = businessId,
                ["outletId"] = outletId,
                ["actorUserId"] = actorUserId
            },
            Entities =
            [
                new AuditEntityInput(
                    "AccountingOutletConfiguration",
                    configurationId.ToString(),
                    operationType,
                    changes)
            ]
        }, cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?> Snapshot(
        AccountingOutletConfiguration configuration) =>
        new Dictionary<string, object?>
        {
            [nameof(configuration.OutletId)] = configuration.OutletId,
            [nameof(configuration.OutletCashAccountId)] = configuration.OutletCashAccountId,
            [nameof(configuration.OutletPettyCashAccountId)] = configuration.OutletPettyCashAccountId,
            [nameof(configuration.OutletBankAccountId)] = configuration.OutletBankAccountId
        };
}
