using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.OtherIncome.Abstractions;
using RetailPOS.Core.Entities;
using MediatR;

namespace RetailPOS.API.Services;

public sealed class OtherIncomePostingAccountResolver(
    IAccountingConfigurationService accountingConfiguration,
    ITenantAccessService tenantAccess)
    : IOtherIncomePostingAccountResolver
{
    public async Task<long> ResolveAccountIdAsync(
        long businessId,
        string accountSlot,
        long? outletId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBusinessScope(businessId);
        var resolution = await accountingConfiguration.ResolvePostingAccountAsync(
            accountSlot,
            outletId);
        cancellationToken.ThrowIfCancellationRequested();
        return resolution.AccountId;
    }

    private void EnsureBusinessScope(long businessId)
    {
        if (tenantAccess.RequireBusinessId() != businessId)
        {
            throw new InvalidOperationException(
                "Other income approval Business scope does not match the active tenant.");
        }
    }
}

public sealed class OtherIncomeApprovalJournalPoster(
    ISender sender,
    ITenantAccessService tenantAccess)
    : IOtherIncomeApprovalJournalPoster
{
    public async Task<long> CreateOrGetAndPostAsync(
        long businessId,
        long actorUserId,
        OtherIncomeApprovalJournalInput journal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantAccess.RequireBusinessId() != businessId)
        {
            throw new InvalidOperationException(
                "Other income approval Business scope does not match the active tenant.");
        }

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
                    null,
                    null,
                    null,
                    journal.Lines.Select(line => new ManualJournalDraftLineInput(
                        line.AccountId,
                        line.Description,
                        line.DebitAmount,
                        line.CreditAmount,
                        line.OutletId,
                        null,
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
