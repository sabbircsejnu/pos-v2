using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record UpdateManualJournalDraftCommand(
    long BusinessId,
    long ActorUserId,
    long JournalId,
    ManualJournalDraftUpdateInput Journal)
    : ICommand<long>;

public sealed class UpdateManualJournalDraftCommandHandler(
    IManualJournalDraftStore store)
    : IRequestHandler<UpdateManualJournalDraftCommand, long>
{
    public Task<long> Handle(
        UpdateManualJournalDraftCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateAsync(
            request.BusinessId,
            request.ActorUserId,
            request.JournalId,
            request.Journal,
            cancellationToken);
}
