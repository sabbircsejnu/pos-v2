using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record RejectManualJournalCommand(
    long BusinessId,
    long ActorUserId,
    long JournalId,
    string Reason)
    : ICommand<long>;

public sealed class RejectManualJournalCommandHandler(
    IManualJournalDraftStore store)
    : IRequestHandler<RejectManualJournalCommand, long>
{
    public Task<long> Handle(
        RejectManualJournalCommand request,
        CancellationToken cancellationToken) =>
        store.RejectAsync(
            request.BusinessId,
            request.ActorUserId,
            request.JournalId,
            request.Reason,
            cancellationToken);
}
