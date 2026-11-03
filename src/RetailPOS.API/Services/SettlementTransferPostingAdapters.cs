using MediatR;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.AccountingSettlements.Abstractions;

namespace RetailPOS.API.Services;

public sealed class SettlementPostingAccountResolver(
    IAccountingConfigurationService accountingConfiguration,
    ITenantAccessService tenantAccess) : ISettlementPostingAccountResolver
{
    public async Task<long> ResolveAsync(
        long businessId,
        string slot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantAccess.RequireBusinessId() != businessId)
        {
            throw new InvalidOperationException(
                "Settlement Business scope does not match the active tenant.");
        }

        var resolution = await accountingConfiguration.ResolvePostingAccountAsync(slot);
        cancellationToken.ThrowIfCancellationRequested();
        return resolution.AccountId;
    }
}

public sealed class SettlementJournalPoster(
    ISender journalCommands,
    ITenantAccessService tenantAccess) : ISettlementJournalPoster
{
    public async Task<long> CreateOrGetAndPostAsync(
        long businessId,
        long actorUserId,
        SettlementJournalInput journal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantAccess.RequireBusinessId() != businessId)
        {
            throw new InvalidOperationException(
                "Settlement Business scope does not match the active tenant.");
        }

        var journalId = await journalCommands.Send(
            new CreateOrGetSystemJournalDraftCommand(
                businessId,
                actorUserId,
                new ManualJournalDraftInput(
                    journal.JournalType,
                    journal.TransactionDate,
                    journal.PostingDate,
                    journal.BusinessDate,
                    journal.ReferenceType,
                    journal.ReferenceId,
                    journal.ReferenceNumber,
                    journal.Description,
                    null,
                    null,
                    null,
                    null,
                    journal.Lines.Select(line => new ManualJournalDraftLineInput(
                        line.AccountId,
                        line.Description,
                        line.DebitAmount,
                        line.CreditAmount,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null)).ToList()),
                journal.IdempotencyDiscriminator),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return await journalCommands.Send(
            new PostJournalEntryCommand(businessId, actorUserId, journalId),
            cancellationToken);
    }
}

public sealed class SettlementJournalReverser(
    ISender journalCommands,
    ITenantAccessService tenantAccess) : ISettlementJournalReverser
{
    public async Task<long> ReverseAsync(
        long businessId,
        long actorUserId,
        long journalEntryId,
        SettlementJournalReversalInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantAccess.RequireBusinessId() != businessId)
        {
            throw new InvalidOperationException(
                "Settlement Business scope does not match the active tenant.");
        }

        return await journalCommands.Send(
            new ReverseJournalEntryCommand(
                businessId,
                actorUserId,
                journalEntryId,
                new ManualJournalReversalInput(
                    input.Reason,
                    input.PostingDate,
                    input.BusinessDate)),
            cancellationToken);
    }
}
