using MediatR;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;

namespace RetailPOS.API.Services;

public sealed class AccountingFinancialYearCloseJournalPostingAdapter(
    ISender sender) : IAccountingFinancialYearCloseJournalPosting
{
    public async Task<long> CreateOrGetSystemDraftAsync(
        long businessId,
        FinancialYearCloseJournalDraft draft,
        long actorUserId,
        string idempotencyDiscriminator,
        CancellationToken cancellationToken)
    {
        var journal = new ManualJournalDraftInput(
            draft.JournalType,
            draft.TransactionDate,
            draft.PostingDate,
            draft.BusinessDate,
            draft.ReferenceType,
            draft.ReferenceId,
            null,
            draft.Description,
            null,
            null,
            null,
            null,
            draft.Lines.Select(line => new ManualJournalDraftLineInput(
                line.AccountId,
                line.Description,
                line.DebitAmount,
                line.CreditAmount,
                null,
                null,
                null,
                null,
                null,
                null)).ToList());
        return await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(
                businessId,
                actorUserId,
                journal,
                idempotencyDiscriminator),
            cancellationToken);
    }

    public async Task<long> PostApprovedYearEndClosingAsync(
        long businessId,
        long journalEntryId,
        long financialYearCloseRunId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        return await sender.Send(
            new PostApprovedYearEndClosingJournalCommand(
                businessId,
                actorUserId,
                journalEntryId,
                financialYearCloseRunId),
            cancellationToken);
    }

    public async Task<long> ReverseYearEndJournalAsync(
        long businessId,
        long journalEntryId,
        string reason,
        DateTime postingDate,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        return await sender.Send(
            new ReverseJournalEntryCommand(
                businessId,
                actorUserId,
                journalEntryId,
                new ManualJournalReversalInput(
                    reason,
                    postingDate.Date,
                    postingDate.Date)),
            cancellationToken);
    }
}
