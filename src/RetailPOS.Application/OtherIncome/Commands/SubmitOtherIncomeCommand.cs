using MediatR;
using RetailPOS.Application.OtherIncome.Abstractions;

namespace RetailPOS.Application.OtherIncome.Commands;

public sealed record SubmitOtherIncomeCommand(
    long BusinessId,
    long ActorUserId,
    long EntryId) : IRequest<long>;

public sealed class SubmitOtherIncomeCommandHandler(IOtherIncomeCommandStore store)
    : IRequestHandler<SubmitOtherIncomeCommand, long>
{
    public Task<long> Handle(
        SubmitOtherIncomeCommand request,
        CancellationToken cancellationToken) =>
        store.SubmitAsync(
            request.BusinessId,
            request.ActorUserId,
            request.EntryId,
            cancellationToken);
}
