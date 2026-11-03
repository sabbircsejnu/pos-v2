using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using MediatR;

namespace RetailPOS.API.Services;

public sealed class BankStatementAdjustmentJournalPoster(
    ISender sender,
    IAccountingConfigurationService configuration)
    : IBankStatementAdjustmentJournalPoster
{
    public async Task<long> ResolvePostingAccountIdAsync(
        string adjustmentType,
        CancellationToken cancellationToken)
    {
        var slot = adjustmentType == RetailPOS.Core.Entities.BankStatementAdjustmentTypes.BankFee
            ? PostingAccountSlots.BankFeeExpense
            : PostingAccountSlots.BankInterestIncome;
        var configured = await configuration.ResolvePostingAccountAsync(slot, null);
        return configured.AccountId;
    }

    public async Task<long> CreateAndPostAsync(
        BankStatementAdjustmentJournalRequest request,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var postingAccountId = request.PostingAccountId;
        var lines = request.AdjustmentType
            == RetailPOS.Core.Entities.BankStatementAdjustmentTypes.BankFee
            ? new List<CreateJournalEntryLineDto>
            {
                new()
                {
                    AccountId = postingAccountId,
                    DebitAmount = request.Amount,
                    Description = "Bank fee adjustment"
                },
                new()
                {
                    AccountId = request.BankChartAccountId,
                    CreditAmount = request.Amount,
                    Description = "Bank fee bank movement"
                }
            }
            : new List<CreateJournalEntryLineDto>
            {
                new()
                {
                    AccountId = request.BankChartAccountId,
                    DebitAmount = request.Amount,
                    Description = "Bank interest bank movement"
                },
                new()
                {
                    AccountId = postingAccountId,
                    CreditAmount = request.Amount,
                    Description = "Bank interest adjustment"
                }
            };
        cancellationToken.ThrowIfCancellationRequested();
        var journalId = await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(
                request.BusinessId,
                actorUserId,
                new ManualJournalDraftInput(
                    "BankStatementAdjustment",
                    request.PostingDate,
                    request.PostingDate,
                    request.PostingDate,
                    "BankStatementAdjustment",
                    request.AdjustmentId.ToString(),
                    null,
                    request.Reason,
                    null,
                    null,
                    null,
                    null,
                    lines.Select(line => new ManualJournalDraftLineInput(
                        line.AccountId,
                        line.Description,
                        line.DebitAmount,
                        line.CreditAmount,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null)).ToArray()),
                $"bank-adjustment:{request.BusinessId}:{request.IdempotencyKey}"),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await sender.Send(
            new PostJournalEntryCommand(request.BusinessId, actorUserId, journalId),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return journalId;
    }

    public async Task<long> ReverseAsync(
        long businessId,
        long journalEntryId,
        string reason,
        DateTime postingDate,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reversalId = await sender.Send(
            new ReverseJournalEntryCommand(
                businessId,
                actorUserId,
                journalEntryId,
                new ManualJournalReversalInput(
                    reason,
                    postingDate,
                    postingDate)),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return reversalId;
    }
}
