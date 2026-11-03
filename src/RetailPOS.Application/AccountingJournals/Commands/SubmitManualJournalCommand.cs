using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record SubmitManualJournalCommand(
    long BusinessId,
    long ActorUserId,
    long JournalId)
    : ICommand<long>;

public sealed class SubmitManualJournalCommandHandler(
    IManualJournalDraftStore store)
    : IRequestHandler<SubmitManualJournalCommand, long>
{
    public Task<long> Handle(
        SubmitManualJournalCommand request,
        CancellationToken cancellationToken) =>
        store.SubmitAsync(
            request.BusinessId,
            request.ActorUserId,
            request.JournalId,
            cancellationToken);
}
