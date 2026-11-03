using MediatR;
using RetailPOS.Application.OtherIncome.Abstractions;

namespace RetailPOS.Application.OtherIncome.Commands;

public sealed record UpdateOtherIncomeCommand(
    long BusinessId,
    long ActorUserId,
    long EntryId,
    OtherIncomeCreationInput Input) : IRequest<long>;

public sealed class UpdateOtherIncomeCommandHandler(IOtherIncomeCommandStore store)
    : IRequestHandler<UpdateOtherIncomeCommand, long>
{
    public Task<long> Handle(
        UpdateOtherIncomeCommand request,
        CancellationToken cancellationToken) =>
        store.UpdateAsync(
            request.BusinessId,
            request.ActorUserId,
            request.EntryId,
            request.Input,
            cancellationToken);
}
