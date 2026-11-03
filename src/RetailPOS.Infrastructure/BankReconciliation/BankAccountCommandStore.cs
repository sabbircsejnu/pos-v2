using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankReconciliation;

public sealed class BankAccountCommandStore(
    RetailPOSDbContext dbContext,
    IAuditService? auditService = null)
    : IBankAccountCommandStore
{
    public async Task<BankAccountReadModel> CreateAsync(
        long businessId,
        long? actorUserId,
        CreateBankAccountInput input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.DisplayName))
            throw new InvalidOperationException("Bank account display name is required.");

        var chartAccount = await dbContext.ChartAccounts
            .SingleOrDefaultAsync(
                row => row.Id == input.ChartAccountId && row.BusinessId == businessId,
                cancellationToken);
        if (chartAccount is null)
            throw new InvalidOperationException(
                "Chart account not found in this business.");
        if (!chartAccount.IsActive
            || !string.Equals(
                chartAccount.AccountType,
                ChartAccountTypes.Asset,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Bank account must use an active Asset chart account.");
        }

        if (input.OutletId.HasValue
            && !await dbContext.Outlets.AnyAsync(
                row => row.Id == input.OutletId.Value
                    && row.BusinessId == businessId,
                cancellationToken))
        {
            throw new InvalidOperationException("Outlet not found in this business.");
        }

        if (await dbContext.BankAccounts.AnyAsync(
                row => row.BusinessId == businessId
                    && row.ChartAccountId == input.ChartAccountId,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "A bank account already exists for this chart account in this business.");
        }

        var now = DateTime.UtcNow;
        var bankAccount = new BankAccount
        {
            BusinessId = businessId,
            ChartAccountId = input.ChartAccountId,
            OutletId = input.OutletId,
            DisplayName = input.DisplayName.Trim(),
            AccountReference = string.IsNullOrWhiteSpace(input.AccountReference)
                ? null
                : input.AccountReference.Trim(),
            IsActive = input.IsActive,
            ReconciliationStartDate = input.ReconciliationStartDate?.Date,
            OpeningStatementBalance = input.OpeningStatementBalance,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId
        };

        dbContext.BankAccounts.Add(bankAccount);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (auditService is not null)
        {
            await auditService.RecordAsync(
                new AuditEventInput
                {
                    ActionType = AuditActionType.Create,
                    Module = AuditModule.Accounts,
                    Summary = $"Bank account created: {bankAccount.Id}",
                    PrimaryEntity = ("BankAccount", bankAccount.Id.ToString()),
                    Metadata = new Dictionary<string, object?>
                    {
                        ["businessId"] = bankAccount.BusinessId,
                        ["actorUserId"] = actorUserId
                    },
                    Entities =
                    [
                        new AuditEntityInput(
                            "BankAccount",
                            bankAccount.Id.ToString(),
                            AuditOperationType.Insert,
                            [
                                ("DisplayName", null, bankAccount.DisplayName),
                                ("ChartAccountId", null, bankAccount.ChartAccountId),
                                ("OutletId", null, bankAccount.OutletId),
                                ("IsActive", null, bankAccount.IsActive),
                                ("ReconciliationStartDate", null, bankAccount.ReconciliationStartDate),
                                ("OpeningStatementBalance", null, bankAccount.OpeningStatementBalance)
                            ])
                    ]
                },
                cancellationToken);
        }

        return new BankAccountReadModel(
            bankAccount.Id,
            bankAccount.DisplayName,
            bankAccount.ChartAccountId,
            bankAccount.OutletId,
            bankAccount.IsActive);
    }
}
