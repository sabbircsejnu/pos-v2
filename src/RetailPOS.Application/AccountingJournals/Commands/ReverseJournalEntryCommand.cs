using RetailPOS.Application.Settings.Abstractions;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record ReverseJournalEntryCommand(
    long BusinessId,
    long ActorUserId,
    long JournalId,
    ManualJournalReversalInput Reversal)
    : ICommand<long>;

public sealed class ReverseJournalEntryCommandHandler(
    IManualJournalDraftStore store,
    ICompanyTimeZoneProvider timeZones)
    : IRequestHandler<ReverseJournalEntryCommand, long>
{
    public async Task<long> Handle(
        ReverseJournalEntryCommand request,
        CancellationToken cancellationToken)
    {
        // Without a chosen date, a reversal posts on today in the Company time zone.
        var reversal = request.Reversal.PostingDate.HasValue
            ? request.Reversal
            : request.Reversal with
            {
                PostingDate = await timeZones.GetTodayAsync(request.BusinessId, cancellationToken)
            };
        return await store.ReverseAsync(
            request.BusinessId,
            request.ActorUserId,
            request.JournalId,
            reversal,
            cancellationToken);
    }

}
