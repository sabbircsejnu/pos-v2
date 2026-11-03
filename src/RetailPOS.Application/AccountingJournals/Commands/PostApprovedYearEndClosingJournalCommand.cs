using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Application.AccountingJournals.Commands;

public sealed record PostApprovedYearEndClosingJournalCommand(
    long BusinessId,
    long ActorUserId,
    long JournalId,
    long FinancialYearCloseRunId) : ICommand<long>;

public sealed class PostApprovedYearEndClosingJournalCommandHandler(
    IManualJournalDraftStore store)
    : IRequestHandler<PostApprovedYearEndClosingJournalCommand, long>
{
    public Task<long> Handle(
        PostApprovedYearEndClosingJournalCommand request,
        CancellationToken cancellationToken) =>
        store.PostApprovedYearEndClosingAsync(
            request.BusinessId,
            request.ActorUserId,
            request.JournalId,
            request.FinancialYearCloseRunId,
            cancellationToken);
}
