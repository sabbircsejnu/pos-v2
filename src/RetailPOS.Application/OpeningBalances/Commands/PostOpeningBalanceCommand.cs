using MediatR;
using RetailPOS.Application.OpeningBalances.Abstractions;

namespace RetailPOS.Application.OpeningBalances.Commands;

public sealed record PostOpeningBalanceCommand(
    long BusinessId,
    long ActorUserId,
    OpeningBalanceInput Input) : IRequest<long>;

public sealed class PostOpeningBalanceCommandHandler(IOpeningBalanceCommandStore store)
    : IRequestHandler<PostOpeningBalanceCommand, long>
{
    public Task<long> Handle(
        PostOpeningBalanceCommand request,
        CancellationToken cancellationToken) =>
        store.PostAsync(
            request.BusinessId,
            request.ActorUserId,
            request.Input,
            cancellationToken);
}

public sealed record PostOpeningBalanceAdjustmentCommand(
    long BusinessId,
    long ActorUserId,
    long OpeningBalanceJournalEntryId,
    OpeningBalanceAdjustmentInput Input) : IRequest<long>;

public sealed class PostOpeningBalanceAdjustmentCommandHandler(
    IOpeningBalanceCommandStore store)
    : IRequestHandler<PostOpeningBalanceAdjustmentCommand, long>
{
    public Task<long> Handle(
        PostOpeningBalanceAdjustmentCommand request,
        CancellationToken cancellationToken) =>
        store.PostAdjustmentAsync(
            request.BusinessId,
            request.ActorUserId,
            request.OpeningBalanceJournalEntryId,
            request.Input,
            cancellationToken);
}
