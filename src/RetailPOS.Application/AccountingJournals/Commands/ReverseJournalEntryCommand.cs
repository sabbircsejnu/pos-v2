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
    IManualJournalDraftStore store)
    : IRequestHandler<ReverseJournalEntryCommand, long>
{
    public Task<long> Handle(
        ReverseJournalEntryCommand request,
        CancellationToken cancellationToken) =>
        store.ReverseAsync(
            request.BusinessId,
            request.ActorUserId,
            request.JournalId,
            request.Reversal,
            cancellationToken);
}
