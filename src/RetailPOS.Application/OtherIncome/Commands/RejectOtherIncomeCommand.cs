using MediatR;
using RetailPOS.Application.OtherIncome.Abstractions;

namespace RetailPOS.Application.OtherIncome.Commands;

public sealed record RejectOtherIncomeCommand(
    long BusinessId,
    long ActorUserId,
    long EntryId,
    string Reason) : IRequest<long>;

public sealed class RejectOtherIncomeCommandHandler(IOtherIncomeCommandStore store)
    : IRequestHandler<RejectOtherIncomeCommand, long>
{
    public Task<long> Handle(
        RejectOtherIncomeCommand request,
        CancellationToken cancellationToken) =>
        store.RejectAsync(
            request.BusinessId,
            request.ActorUserId,
            request.EntryId,
            request.Reason,
            cancellationToken);
}
