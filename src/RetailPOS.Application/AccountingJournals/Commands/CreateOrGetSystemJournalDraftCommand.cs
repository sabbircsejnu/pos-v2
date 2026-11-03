using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record CreateOrGetSystemJournalDraftCommand(
    long BusinessId,
    long ActorUserId,
    ManualJournalDraftInput Journal,
    string? IdempotencyDiscriminator) : ICommand<long>;

public sealed class CreateOrGetSystemJournalDraftCommandHandler(
    IManualJournalDraftStore journalStore)
    : IRequestHandler<CreateOrGetSystemJournalDraftCommand, long>
{
    public Task<long> Handle(
        CreateOrGetSystemJournalDraftCommand request,
        CancellationToken cancellationToken) =>
        journalStore.CreateOrGetSystemDraftAsync(
            request.BusinessId,
            request.ActorUserId,
            request.Journal,
            request.IdempotencyDiscriminator,
            cancellationToken);
}
