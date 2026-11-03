using MediatR;
using RetailPOS.Application.OtherIncome.Abstractions;

namespace RetailPOS.Application.OtherIncome.Commands;

public sealed record CancelOtherIncomeCommand(
    long BusinessId,
    long ActorUserId,
    long EntryId,
    string Reason) : IRequest<long>;

public sealed class CancelOtherIncomeCommandHandler(IOtherIncomeCommandStore store)
    : IRequestHandler<CancelOtherIncomeCommand, long>
{
    public Task<long> Handle(
        CancelOtherIncomeCommand request,
        CancellationToken cancellationToken) =>
        store.CancelAsync(
            request.BusinessId,
            request.ActorUserId,
            request.EntryId,
            request.Reason,
            cancellationToken);
}
