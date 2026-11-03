using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record CancelManualJournalCommand(
    long BusinessId,
    long ActorUserId,
    long JournalId,
    string? Reason)
    : ICommand<long>;

public sealed class CancelManualJournalCommandHandler(
    IManualJournalDraftStore store)
    : IRequestHandler<CancelManualJournalCommand, long>
{
    public Task<long> Handle(
        CancelManualJournalCommand request,
        CancellationToken cancellationToken) =>
        store.CancelAsync(
            request.BusinessId,
            request.ActorUserId,
            request.JournalId,
            request.Reason,
            cancellationToken);
}
