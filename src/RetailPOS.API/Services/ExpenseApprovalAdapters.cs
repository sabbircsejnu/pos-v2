using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Core.Entities;
using MediatR;

namespace RetailPOS.API.Services;

public sealed class ExpenseApprovalPostingAccountResolver(
    IAccountingConfigurationService accountingConfiguration,
    ITenantAccessService tenantAccess)
    : IExpensePostingAccountResolver
{
    public async Task<long> ResolveAccountIdAsync(
        long businessId,
        string accountSlot,
        long? outletId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBusinessScope(businessId);
        var resolution = await accountingConfiguration.ResolvePostingAccountAsync(accountSlot, outletId);
        cancellationToken.ThrowIfCancellationRequested();
        return resolution.AccountId;
    }

    private void EnsureBusinessScope(long businessId)
    {
        if (tenantAccess.RequireBusinessId() != businessId)
            throw new InvalidOperationException("Expense approval Business scope does not match the active tenant.");
    }
}

public sealed class ExpenseApprovalJournalPoster(
    ISender sender,
    ITenantAccessService tenantAccess)
    : IExpenseApprovalJournalPoster
{
    public async Task<long> CreateOrGetAndPostAsync(
        long businessId,
        long actorUserId,
        ExpenseApprovalJournalInput journal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantAccess.RequireBusinessId() != businessId)
            throw new InvalidOperationException("Expense approval Business scope does not match the active tenant.");

        var journalId = await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(
                businessId,
                actorUserId,
                new ManualJournalDraftInput(
                    AccountingJournalTypes.SystemJournal,
                    journal.TransactionDate,
                    journal.PostingDate,
                    journal.BusinessDate,
                    journal.ReferenceType,
                    journal.ReferenceId,
                    journal.ReferenceNumber,
                    journal.Description,
                    journal.OutletId,
                    journal.WarehouseId,
                    null,
                    null,
                    journal.Lines.Select(line => new ManualJournalDraftLineInput(
                        line.AccountId,
                        line.Description,
                        line.DebitAmount,
                        line.CreditAmount,
                        line.OutletId,
                        line.WarehouseId,
                        null,
                        null,
                        null,
                        null)).ToArray()),
                null),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await sender.Send(
            new PostJournalEntryCommand(businessId, actorUserId, journalId),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return journalId;
    }
}
