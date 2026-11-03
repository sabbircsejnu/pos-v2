using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record PostJournalEntryCommand(
    long BusinessId,
    long ActorUserId,
    long JournalId)
    : ICommand<long>;

public sealed class PostJournalEntryCommandHandler(
    IManualJournalDraftStore store)
    : IRequestHandler<PostJournalEntryCommand, long>
{
    public Task<long> Handle(
        PostJournalEntryCommand request,
        CancellationToken cancellationToken) =>
        store.PostAsync(
            request.BusinessId,
            request.ActorUserId,
            request.JournalId,
            cancellationToken);
}
