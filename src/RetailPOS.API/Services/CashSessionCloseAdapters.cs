using MediatR;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

public sealed class CashSessionVarianceJournalPoster(
    ISender? journalCommands,
    IAccountingConfigurationService? accountingConfigurationService)
    : ICashSessionVarianceJournalPoster
{
    public async Task<long?> PostAsync(
        CashSessionVarianceJournalRequest request,
        CancellationToken cancellationToken)
    {
        if (journalCommands is null || accountingConfigurationService is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        var varianceAmount = MonetaryRounding.ToCurrency(Math.Abs(request.Difference));
        var cashAccount = await accountingConfigurationService.ResolvePostingAccountAsync(
            PostingAccountSlots.Cash,
            request.OutletId);
        cancellationToken.ThrowIfCancellationRequested();
        var gainAccount = await accountingConfigurationService.ResolvePostingAccountAsync(
            PostingAccountSlots.CashDifferenceGain,
            request.OutletId);
        cancellationToken.ThrowIfCancellationRequested();
        var lossAccount = await accountingConfigurationService.ResolvePostingAccountAsync(
            PostingAccountSlots.CashDifferenceLoss,
            request.OutletId);
        cancellationToken.ThrowIfCancellationRequested();

        var lines = new List<ManualJournalDraftLineInput>();
        if (request.Difference > 0)
        {
            lines.Add(new ManualJournalDraftLineInput(
                cashAccount.AccountId,
                "Cash variance increase",
                varianceAmount,
                0,
                request.OutletId,
                null,
                null,
                null,
                null,
                null));
            lines.Add(new ManualJournalDraftLineInput(
                gainAccount.AccountId,
                "Cash difference gain",
                0,
                varianceAmount,
                request.OutletId,
                null,
                null,
                null,
                null,
                null));
        }
        else
        {
            lines.Add(new ManualJournalDraftLineInput(
                lossAccount.AccountId,
                "Cash difference loss",
                varianceAmount,
                0,
                request.OutletId,
                null,
                null,
                null,
                null,
                null));
            lines.Add(new ManualJournalDraftLineInput(
                cashAccount.AccountId,
                "Cash variance reduction",
                0,
                varianceAmount,
                request.OutletId,
                null,
                null,
                null,
                null,
                null));
        }

        var journal = new ManualJournalDraftInput(
            request.Difference > 0
                ? "CashDifferenceSurplusPosting"
                : "CashDifferenceShortagePosting",
            request.TransactionTimestamp,
            request.TransactionTimestamp,
            request.BusinessDate,
            "CashSessionClose",
            request.SessionId.ToString(),
            request.SessionId.ToString(),
            request.Difference > 0
                ? $"Cash session surplus posting for session {request.SessionId}"
                : $"Cash session shortage posting for session {request.SessionId}",
            request.OutletId,
            null,
            request.TerminalId,
            request.SessionId,
            lines);

        cancellationToken.ThrowIfCancellationRequested();
        var journalId = await journalCommands.Send(
            new CreateOrGetSystemJournalDraftCommand(
                request.BusinessId,
                request.ActorUserId,
                journal,
                $"cash-difference:{request.ApprovalId}"),
            cancellationToken);
        return await journalCommands.Send(
            new PostJournalEntryCommand(request.BusinessId, request.ActorUserId, journalId),
            cancellationToken);
    }
}

public sealed class CashSessionClosedPublisherAdapter(ISaleEventPublisher eventPublisher)
    : ICashSessionClosedPublisher
{
    public Task PublishClosedAsync(
        CashSessionSnapshot session,
        long closedByUserId,
        decimal difference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = eventPublisher.PublishCashSessionClosedAsync(new CashSessionClosedEvent
        {
            CashSessionId = session.Id,
            OutletId = session.OutletId,
            TerminalId = session.TerminalId,
            ClosedByUserId = closedByUserId,
            ClosedAt = session.ClosedAt ?? DateTime.UtcNow,
            DifferenceAmount = difference
        });
        return Task.CompletedTask;
    }
}
