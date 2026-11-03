using MediatR;
using RetailPOS.Application.OtherIncome.Abstractions;

namespace RetailPOS.Application.OtherIncome.Commands;

public sealed record CreateOtherIncomeCommand(
    long BusinessId,
    long ActorUserId,
    OtherIncomeCreationInput Input) : IRequest<long>;

public sealed class CreateOtherIncomeCommandHandler(IOtherIncomeCommandStore store)
    : IRequestHandler<CreateOtherIncomeCommand, long>
{
    public Task<long> Handle(
        CreateOtherIncomeCommand request,
        CancellationToken cancellationToken) =>
        store.CreateAsync(
            request.BusinessId,
            request.ActorUserId,
            request.Input,
            cancellationToken);
}
