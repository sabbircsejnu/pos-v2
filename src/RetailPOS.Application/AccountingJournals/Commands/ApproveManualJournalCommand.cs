using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record ApproveManualJournalCommand(
    long BusinessId,
    long ActorUserId,
    long JournalId,
    bool IsBusinessOwnerOrSuperAdmin)
    : ICommand<long>;

public sealed class ApproveManualJournalCommandHandler(
    IManualJournalDraftStore store)
    : IRequestHandler<ApproveManualJournalCommand, long>
{
    public Task<long> Handle(
        ApproveManualJournalCommand request,
        CancellationToken cancellationToken) =>
        store.ApproveAsync(
            request.BusinessId,
            request.ActorUserId,
            request.JournalId,
            request.IsBusinessOwnerOrSuperAdmin,
            cancellationToken);
}
