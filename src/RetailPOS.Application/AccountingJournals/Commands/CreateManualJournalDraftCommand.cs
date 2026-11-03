using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record CreateManualJournalDraftCommand(
    long BusinessId,
    long ActorUserId,
    ManualJournalDraftInput Journal)
    : ICommand<long>;

public sealed class CreateManualJournalDraftCommandHandler(
    IManualJournalDraftStore store)
    : IRequestHandler<CreateManualJournalDraftCommand, long>
{
    public Task<long> Handle(
        CreateManualJournalDraftCommand request,
        CancellationToken cancellationToken) =>
        store.CreateAsync(
            request.BusinessId,
            request.ActorUserId,
            request.Journal,
            cancellationToken);
}
